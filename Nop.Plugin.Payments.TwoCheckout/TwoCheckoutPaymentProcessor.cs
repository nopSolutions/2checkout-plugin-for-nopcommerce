using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.TwoCheckout.Components;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Html;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;

namespace Nop.Plugin.Payments.TwoCheckout
{
    /// <summary>
    /// Represents 2Checkout payment processor
    /// </summary>
    public class TwoCheckoutPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly IAddressService _addressService;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ILocalizationService _localizationService;
        private readonly IHtmlFormatter _htmlFormatter;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public TwoCheckoutPaymentProcessor(CurrencySettings currencySettings,
            IActionContextAccessor actionContextAccessor,
            IAddressService addressService,
            ICountryService countryService,
            ICurrencyService currencyService,
            ILocalizationService localizationService,
            IHtmlFormatter htmlFormatter,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IProductService productService,
            ISettingService settingService,
            IStateProvinceService stateProvinceService,
            IUrlHelperFactory urlHelperFactory,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _currencySettings = currencySettings;
            _actionContextAccessor = actionContextAccessor;
            _addressService = addressService;
            _countryService = countryService;
            _currencyService = currencyService;
            _localizationService = localizationService;
            _htmlFormatter = htmlFormatter;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _productService = productService;
            _settingService = settingService;
            _stateProvinceService = stateProvinceService;
            _urlHelperFactory = urlHelperFactory;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Handle payment
        /// </summary>
        /// <param name="isIpn">Whether the request is IPN</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public async Task<int?> HandleTransactionAsync(bool isIpn)
        {
            try
            {
                var request = _actionContextAccessor.ActionContext.HttpContext.Request;
                var parameters = request.HasFormContentType && request.Form.Keys.Any()
                    ? request.Form.ToDictionary(param => param.Key, param => param.Value)
                    : request.Query.ToDictionary(param => param.Key, param => param.Value);

                //define local function to get a value from the request Form or from the Query parameters
                string getValue(string key) => parameters.TryGetValue(key, out var value) ? value.ToString() : string.Empty;

                //try to get an order by the number or by the identifier (used in previous plugin versions)
                var customOrderNumber = getValue(isIpn ? "item_id_1" : "x_invoice_num");
                var order = await _orderService.GetOrderByCustomOrderNumberAsync(customOrderNumber)
                    ?? await _orderService.GetOrderByIdAsync(int.TryParse(customOrderNumber, out var orderId) ? orderId : 0)
                    ?? throw new NopException($"Order '{customOrderNumber}' not found");

                //save request info as order note for debug purposes
                var note = parameters.Aggregate(isIpn ? "2Checkout IPN" : "2Checkout redirect",
                    (text, param) => $"{text}{Environment.NewLine}{param.Key}: {param.Value}");
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = note,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                if (!isIpn)
                    return order.Id;

                //verify the passed data by comparing MD5 hashes
                if (_twoCheckoutPaymentSettings.UseMd5Hashing)
                {
                    var stringToHash = $"{getValue("sale_id")}" +
                        $"{_twoCheckoutPaymentSettings.AccountNumber}" +
                        $"{getValue("invoice_id")}" +
                        $"{_twoCheckoutPaymentSettings.SecretWord}";
                    var data = MD5.Create().ComputeHash(Encoding.Default.GetBytes(stringToHash));
                    var sBuilder = new StringBuilder();
                    foreach (var t in data)
                    {
                        sBuilder.Append(t.ToString("x2"));
                    }

                    var computedHash = sBuilder.ToString();
                    var receivedHash = getValue("md5_hash");

                    if (computedHash.ToUpperInvariant() != receivedHash.ToUpperInvariant())
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

                //check payment status
                var newPaymentStatus = PaymentStatus.Pending;
                var messageType = getValue("message_type");
                var invoiceStatus = getValue("invoice_status");
                var fraudStatus = getValue("fraud_status");
                var paymentType = getValue("payment_type");

                if (messageType.ToUpperInvariant() == "FRAUD_STATUS_CHANGED"
                    && fraudStatus == "pass"
                    && (invoiceStatus == "approved" || invoiceStatus == "deposited" || paymentType == "paypal ec"))
                {
                    newPaymentStatus = PaymentStatus.Paid;
                }

                //mark order as paid
                if (newPaymentStatus == PaymentStatus.Paid && _orderProcessingService.CanMarkOrderAsPaid(order))
                    await _orderProcessingService.MarkOrderAsPaidAsync(order);

                return order.Id;
            }
            catch (Exception exception)
            {
                throw new NopException($"{TwoCheckoutDefaults.SystemName} error. {exception.Message}", exception);
            }
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
            var builder = new StringBuilder();

            builder.AppendFormat("{0}?id_type=1", TwoCheckoutDefaults.ServiceUrl);

            //products
            var orderProducts = await _orderService.GetOrderItemsAsync(postProcessPaymentRequest.Order.Id);
            for (var i = 0; i < orderProducts.Count; i++)
            {
                var pNum = i + 1;
                var orderItem = orderProducts[i];
                var product = await _productService.GetProductByIdAsync(orderProducts[i].ProductId);

                var cProd = $"c_prod_{pNum}";
                var cProdValue = $"{product.Sku},{orderItem.Quantity}";
                builder.AppendFormat("&{0}={1}", cProd, cProdValue);

                var cName = $"c_name_{pNum}";
                var cNameValue = product.Name;
                builder.AppendFormat("&{0}={1}", WebUtility.UrlEncode(cName), WebUtility.UrlEncode(cNameValue));

                var cDescription = $"c_description_{pNum}";
                var cDescriptionValue = cNameValue;
                if (!string.IsNullOrEmpty(orderItem.AttributeDescription))
                    cDescriptionValue = _htmlFormatter.StripTags($"{cDescriptionValue}. {orderItem.AttributeDescription}");
                builder.AppendFormat("&{0}={1}", WebUtility.UrlEncode(cDescription), WebUtility.UrlEncode(cDescriptionValue));

                var cPrice = $"c_price_{pNum}";
                var cPriceValue = orderItem.UnitPriceInclTax.ToString("0.00", CultureInfo.InvariantCulture);
                builder.AppendFormat("&{0}={1}", cPrice, cPriceValue);

                var cTangible = $"c_tangible_{pNum}";
                var cTangibleValue = product.IsDownload ? "N" : "Y";
                builder.AppendFormat("&{0}={1}", cTangible, cTangibleValue);
            }

            builder.AppendFormat("&x_login={0}", _twoCheckoutPaymentSettings.AccountNumber);
            builder.AppendFormat("&sid={0}", _twoCheckoutPaymentSettings.AccountNumber);
            builder.AppendFormat("&x_amount={0}", postProcessPaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));
            var currency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);
            builder.AppendFormat("&currency_code={0}", currency?.CurrencyCode);
            builder.AppendFormat("&x_invoice_num={0}", postProcessPaymentRequest.Order.CustomOrderNumber);

            if (_twoCheckoutPaymentSettings.UseSandbox)
                builder.AppendFormat("&demo=Y");

            var billingAddress = await _addressService.GetAddressByIdAsync(postProcessPaymentRequest.Order.BillingAddressId);
            if (billingAddress != null)
            {
                var country = await _countryService.GetCountryByIdAsync(billingAddress.CountryId ?? 0);
                var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);
                builder.AppendFormat("&x_First_Name={0}", WebUtility.UrlEncode(billingAddress.FirstName ?? string.Empty));
                builder.AppendFormat("&x_Last_Name={0}", WebUtility.UrlEncode(billingAddress.LastName ?? string.Empty));
                builder.AppendFormat("&x_Address={0}", WebUtility.UrlEncode(billingAddress.Address1 ?? string.Empty));
                builder.AppendFormat("&x_City={0}", WebUtility.UrlEncode(billingAddress.City ?? string.Empty));
                builder.AppendFormat("&x_State={0}", WebUtility.UrlEncode(state?.Abbreviation ?? string.Empty));
                builder.AppendFormat("&x_Country={0}", WebUtility.UrlEncode(country?.ThreeLetterIsoCode ?? string.Empty));
                builder.AppendFormat("&x_Zip={0}", WebUtility.UrlEncode(billingAddress.ZipPostalCode ?? string.Empty));
                builder.AppendFormat("&x_EMail={0}", WebUtility.UrlEncode(billingAddress.Email ?? string.Empty));
                builder.AppendFormat("&x_Phone={0}", WebUtility.UrlEncode(CommonHelper.EnsureNumericOnly(billingAddress.PhoneNumber) ?? string.Empty));
            }

            var redirectUrl = builder.ToString();
            _actionContextAccessor.ActionContext.HttpContext.Response.Redirect(redirectUrl);
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
            if (order == null)
                throw new ArgumentNullException(nameof(order));

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
                LogIpnErrors = true
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
}