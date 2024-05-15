using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.TwoCheckout.Components;
using Nop.Plugin.Payments.TwoCheckout.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.TwoCheckout;

/// <summary>
/// Represents 2Checkout payment processor
/// </summary>
public class TwoCheckoutPaymentProcessor : BasePlugin, IPaymentMethod
{
    #region Fields

    private readonly IActionContextAccessor _actionContextAccessor;
    private readonly ILocalizationService _localizationService;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IOrderTotalCalculationService _orderTotalCalculationService;
    private readonly IPaymentManager _paymentManager;
    private readonly ISettingService _settingService;
    private readonly IUrlHelperFactory _urlHelperFactory;
    private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

    #endregion

    #region Ctor

    public TwoCheckoutPaymentProcessor(IActionContextAccessor actionContextAccessor,
        ILocalizationService localizationService,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IOrderTotalCalculationService orderTotalCalculationService,
        IPaymentManager paymentManager,
        ISettingService settingService,
        IUrlHelperFactory urlHelperFactory,
        TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
    {
        _actionContextAccessor = actionContextAccessor;
        _localizationService = localizationService;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _orderTotalCalculationService = orderTotalCalculationService;
        _paymentManager = paymentManager;
        _settingService = settingService;
        _urlHelperFactory = urlHelperFactory;
        _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle IPN/INS request
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleIpnRequestAsync()
    {
        var request = _actionContextAccessor.ActionContext.HttpContext.Request;
        var form = request.HasFormContentType ? await request.ReadFormAsync() : new FormCollection(null);
        var parameters = request.HasFormContentType && form.Keys.Any()
            ? form.ToDictionary(param => param.Key.ToLower(), param => param.Value)
            : request.Query.ToDictionary(param => param.Key.ToLower(), param => param.Value);

        //define local function to get a value from the request Form or from the Query parameters
        string getValue(string key) => parameters.TryGetValue(key.ToLower(), out var value) ? value.ToString() : string.Empty;

        //try to get an order by the number or by the identifier (used in previous plugin versions)
        var customOrderNumber = getValue(_twoCheckoutPaymentSettings.UseConvertPlus ? "vendor_order_id" : "item_id_1");
        if (string.IsNullOrEmpty(customOrderNumber))
            customOrderNumber = getValue("x_invoice_num");
        var order = await _orderService.GetOrderByCustomOrderNumberAsync(customOrderNumber)
            ?? await _orderService.GetOrderByIdAsync(int.TryParse(customOrderNumber, out var orderId) ? orderId : 0)
            ?? throw new NopException($"Order '{customOrderNumber}' not found");

        //save request info as order note for debug purposes
        var note = parameters.Aggregate(_twoCheckoutPaymentSettings.UseConvertPlus ? "ConvertPlus INS:" : "2Checkout IPN:",
            (text, param) => $"{text}{Environment.NewLine}{param.Key}: {param.Value}");
        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = note,
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });

        //verify the passed data by comparing MD5 hashes
        if (_twoCheckoutPaymentSettings.UseMd5Hashing)
        {
            var stringToHash =
                _twoCheckoutPaymentSettings.UseConvertPlus
                ?
                $"{getValue("sale_id")}" +
                $"{_twoCheckoutPaymentSettings.AccountNumber}" +
                $"{getValue("invoice_id")}" +
                $"{_twoCheckoutPaymentSettings.SecretWord}"
                :
                $"{_twoCheckoutPaymentSettings.SecretWord}" +
                $"{_twoCheckoutPaymentSettings.AccountNumber}" +
                $"{(_twoCheckoutPaymentSettings.UseSandbox ? "1" : getValue("order_number"))}" +
                $"{getValue("x_amount")}";

            var data = MD5.HashData(Encoding.Default.GetBytes(stringToHash));
            var sBuilder = new StringBuilder();
            foreach (var t in data)
            {
                sBuilder.Append(t.ToString("x2"));
            }

            var computedHash = sBuilder.ToString();
            var receivedHash = getValue(_twoCheckoutPaymentSettings.UseConvertPlus ? "md5_hash" : "x_md5_hash");

            if (!string.Equals(computedHash, receivedHash, StringComparison.InvariantCultureIgnoreCase))
            {
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = $"Computed hash '{computedHash}' is not equal to received hash '{receivedHash}'",
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                throw new NopException("Hash validation failed");
            }
        }

        var newPaymentStatus = PaymentStatus.Pending;

        var messageType = getValue("message_type");
        var invoiceStatus = getValue("invoice_status");
        var fraudStatus = getValue("fraud_status");
        var paymentType = getValue("payment_type");

        if (string.Equals(messageType, "FRAUD_STATUS_CHANGED", StringComparison.InvariantCultureIgnoreCase)
            && fraudStatus == "pass"
            && (invoiceStatus == "approved" || invoiceStatus == "deposited" || paymentType == "paypal ec"))
        {
            newPaymentStatus = PaymentStatus.Paid;
        }

        //mark order as paid
        if (newPaymentStatus == PaymentStatus.Paid && _orderProcessingService.CanMarkOrderAsPaid(order))
            await _orderProcessingService.MarkOrderAsPaidAsync(order);
    }

    /// <summary>
    /// Process a payment
    /// </summary>
    /// <param name="processPaymentRequest">Payment info required for an order processing</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the process payment result
    /// </returns>
    public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        return Task.FromResult(new ProcessPaymentResult());
    }

    /// <summary>
    /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
    /// </summary>
    /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
    {
        var url = await _paymentManager.GetRequestUrlAsync(postProcessPaymentRequest.Order);

        _actionContextAccessor.ActionContext.HttpContext.Response.Redirect(url);
    }

    /// <summary>
    /// Returns a value indicating whether payment method should be hidden during checkout
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the rue - hide; false - display.
    /// </returns>
    public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
    {
        //you can put any logic here
        //for example, hide this payment method if all products in the cart are downloadable
        //or hide this payment method if current customer is from certain country
        return Task.FromResult(false);
    }

    /// <summary>
    /// Gets additional handling fee
    /// </summary>
    /// <param name="cart">Shopping cart</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the additional handling fee
    /// </returns>
    public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
    {
        return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
            _twoCheckoutPaymentSettings.AdditionalFee, _twoCheckoutPaymentSettings.AdditionalFeePercentage);
    }

    /// <summary>
    /// Captures payment
    /// </summary>
    /// <param name="capturePaymentRequest">Capture payment request</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the capture payment result
    /// </returns>
    public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
    {
        return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
    }

    /// <summary>
    /// Refunds a payment
    /// </summary>
    /// <param name="refundPaymentRequest">Request</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the result
    /// </returns>
    public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
    {
        return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
    }

    /// <summary>
    /// Voids a payment
    /// </summary>
    /// <param name="voidPaymentRequest">Request</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the result
    /// </returns>
    public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
    {
        return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
    }

    /// <summary>
    /// Process recurring payment
    /// </summary>
    /// <param name="processPaymentRequest">Payment info required for an order processing</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the process payment result
    /// </returns>
    public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
    {
        return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    /// <summary>
    /// Cancels a recurring payment
    /// </summary>
    /// <param name="cancelPaymentRequest">Request</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the result
    /// </returns>
    public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
    {
        return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
    }

    /// <summary>
    /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
    /// </summary>
    /// <param name="order">Order</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the result
    /// </returns>
    public Task<bool> CanRePostProcessPaymentAsync(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);

        //do not allow reposting (it can take up to several hours until your order is reviewed
        return Task.FromResult(false);
    }

    /// <summary>
    /// Validate payment form
    /// </summary>
    /// <param name="form">The parsed form values</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the list of validating errors
    /// </returns>
    public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
    {
        return Task.FromResult<IList<string>>(new List<string>());
    }

    /// <summary>
    /// Get payment information
    /// </summary>
    /// <param name="form">The parsed form values</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the payment info holder
    /// </returns>
    public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
    {
        return Task.FromResult(new ProcessPaymentRequest());
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext).RouteUrl(TwoCheckoutDefaults.ConfigurationRouteName);
    }

    /// <summary>
    /// Gets a type of a view component for displaying plugin in public store ("payment info" checkout step)
    /// </summary>
    /// <returns>View component type</returns>
    public Type GetPublicViewComponent()
    {
        return typeof(PaymentTwoCheckoutViewComponent);
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        await _settingService.SaveSettingAsync(new TwoCheckoutPaymentSettings()
        {
            UseSandbox = true,
            UseMd5Hashing = true,
            LogIpnErrors = true,
            UseConvertPlus = true
        });

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Payments.2Checkout.AccountNumber"] = "Account number",
            ["Plugins.Payments.2Checkout.AccountNumber.Hint"] = "Enter account number.",
            ["Plugins.Payments.2Checkout.AccountNumber.Required"] = "Account number is required",
            ["Plugins.Payments.2Checkout.AdditionalFee"] = "Additional fee",
            ["Plugins.Payments.2Checkout.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
            ["Plugins.Payments.2Checkout.AdditionalFeePercentage"] = "Additional fee. Use percentage",
            ["Plugins.Payments.2Checkout.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
            ["Plugins.Payments.2Checkout.PaymentMethodDescription"] = "You will be redirected to 2Checkout site to complete the order.",
            ["Plugins.Payments.2Checkout.RedirectionTip"] = "You will be redirected to 2Checkout site to complete the order.",
            ["Plugins.Payments.2Checkout.SecretWord"] = "Secret Word",
            ["Plugins.Payments.2Checkout.SecretWord.Hint"] = "Enter secret word.",
            ["Plugins.Payments.2Checkout.SecretWord.Required"] = "Secret word is required",
            ["Plugins.Payments.2Checkout.UseSandbox"] = "Test mode",
            ["Plugins.Payments.2Checkout.UseSandbox.Hint"] = "Check to enable test orders."
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        await _settingService.DeleteSettingAsync<TwoCheckoutPaymentSettings>();
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.2Checkout");

        await base.UninstallAsync();
    }

    /// <summary>
    /// Gets a payment method description that will be displayed on checkout pages in the public store
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task<string> GetPaymentMethodDescriptionAsync()
    {
        return await _localizationService.GetResourceAsync("Plugins.Payments.2Checkout.PaymentMethodDescription");
    }

    #endregion

    #region Properies

    /// <summary>
    /// Gets a value indicating whether capture is supported
    /// </summary>
    public bool SupportCapture => false;

    /// <summary>
    /// Gets a value indicating whether partial refund is supported
    /// </summary>
    public bool SupportPartiallyRefund => false;

    /// <summary>
    /// Gets a value indicating whether refund is supported
    /// </summary>
    public bool SupportRefund => false;

    /// <summary>
    /// Gets a value indicating whether void is supported
    /// </summary>
    public bool SupportVoid => false;

    /// <summary>
    /// Gets a recurring payment type of payment method
    /// </summary>
    public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

    /// <summary>
    /// Gets a payment method type
    /// </summary>
    public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

    /// <summary>
    /// Gets a value indicating whether we should display a payment information page for this plugin
    /// </summary>
    public bool SkipPaymentInfo => false;

    #endregion
}