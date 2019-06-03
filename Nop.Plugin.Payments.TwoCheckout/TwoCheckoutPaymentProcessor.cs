using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Html;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
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
        private readonly ICurrencyService _currencyService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public TwoCheckoutPaymentProcessor(CurrencySettings currencySettings,
            ICurrencyService currencyService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            IWebHelper webHelper,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _currencySettings = currencySettings;
            _currencyService = currencyService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _paymentService = paymentService;
            _settingService = settingService;
            _webHelper = webHelper;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var builder = new StringBuilder();

            var purchaseUrl = _twoCheckoutPaymentSettings.UseSandbox
                ? "https://sandbox.2checkout.com/checkout/purchase"
                : "https://www.2checkout.com/checkout/purchase";

            builder.AppendFormat("{0}?id_type=1", purchaseUrl);

            //products
            var orderProducts = postProcessPaymentRequest.Order.OrderItems.ToList();

            for (var i = 0; i < orderProducts.Count; i++)
            {
                var pNum = i + 1;
                var orderItem = orderProducts[i];
                var product = orderProducts[i].Product;

                var cProd = $"c_prod_{pNum}";
                var cProdValue = $"{product.Sku},{orderItem.Quantity}";
                builder.AppendFormat("&{0}={1}", cProd, cProdValue);

                var cName = $"c_name_{pNum}";
                var cNameValue = _localizationService.GetLocalized(product, entity => entity.Name);
                builder.AppendFormat("&{0}={1}", WebUtility.UrlEncode(cName), WebUtility.UrlEncode(cNameValue));

                var cDescription = $"c_description_{pNum}";
                var cDescriptionValue = cNameValue;
                if (!string.IsNullOrEmpty(orderItem.AttributeDescription))
                    cDescriptionValue = HtmlHelper.StripTags($"{cDescriptionValue}. {orderItem.AttributeDescription}");
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
            builder.AppendFormat("&currency_code={0}", _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode);
            builder.AppendFormat("&x_invoice_num={0}", postProcessPaymentRequest.Order.CustomOrderNumber);

            if (_twoCheckoutPaymentSettings.UseSandbox)
                builder.AppendFormat("&demo=Y");

            builder.AppendFormat("&x_First_Name={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.FirstName ?? string.Empty));
            builder.AppendFormat("&x_Last_Name={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.LastName ?? string.Empty));
            builder.AppendFormat("&x_Address={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.Address1 ?? string.Empty));
            builder.AppendFormat("&x_City={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.City ?? string.Empty));
            builder.AppendFormat("&x_State={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.StateProvince?.Abbreviation ?? string.Empty));
            builder.AppendFormat("&x_Country={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.Country?.ThreeLetterIsoCode ?? string.Empty));
            builder.AppendFormat("&x_Zip={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.ZipPostalCode ?? string.Empty));
            builder.AppendFormat("&x_EMail={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.Email ?? string.Empty));
            builder.AppendFormat("&x_Phone={0}",
                WebUtility.UrlEncode(postProcessPaymentRequest.Order.BillingAddress?.PhoneNumber ?? string.Empty));

            _httpContextAccessor.HttpContext.Response.Redirect(builder.ToString());
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _paymentService.CalculateAdditionalFee(cart,
                _twoCheckoutPaymentSettings.AdditionalFee, _twoCheckoutPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //do not allow reposting (it can take up to several hours until your order is reviewed
            return false;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentTwoCheckout/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return TwoCheckoutDefaults.PAYMENT_INFO_VIEW_COMPONENT_NAME;
        }

        /// <summary>
        /// Install plugin
        /// </summary>
        public override void Install()
        {
            //settings
            _settingService.SaveSetting(new TwoCheckoutPaymentSettings()
            {
                UseSandbox = true,
                UseMd5Hashing = true
            });

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AccountNumber", "Account number");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AccountNumber.Hint", "Enter account number.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.PaymentMethodDescription", "You will be redirected to 2Checkout site to complete the order.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.RedirectionTip", "You will be redirected to 2Checkout site to complete the order.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.SecretWord", "Secret Word");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.SecretWord.Hint", "Enter secret word.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.UseSandbox", "Use Sandbox");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.2Checkout.UseSandbox.Hint", "Check to enable Sandbox (testing environment).");

            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<TwoCheckoutPaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AccountNumber");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AccountNumber.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.PaymentMethodDescription");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.SecretWord");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.SecretWord.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.UseSandbox");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.2Checkout.UseSandbox.Hint");

            base.Uninstall();
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

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.2Checkout.PaymentMethodDescription");

        #endregion
    }
}