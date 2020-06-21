using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Html;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
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
        private readonly IOrderService _orderService;
        private readonly IPaymentService _paymentService;
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
            IOrderService orderService,
            IPaymentService paymentService,
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
            _orderService = orderService;
            _paymentService = paymentService;
            _productService = productService;
            _settingService = settingService;
            _stateProvinceService = stateProvinceService;
            _urlHelperFactory = urlHelperFactory;
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

            builder.AppendFormat("{0}?id_type=1", TwoCheckoutDefaults.ServiceUrl);

            //products
            var orderProducts = _orderService.GetOrderItems(postProcessPaymentRequest.Order.Id);
            for (var i = 0; i < orderProducts.Count; i++)
            {
                var pNum = i + 1;
                var orderItem = orderProducts[i];
                var product = _productService.GetProductById(orderProducts[i].ProductId);

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

            var billingAddress = _addressService.GetAddressById(postProcessPaymentRequest.Order.BillingAddressId);
            if (billingAddress != null)
            {
                var country = _countryService.GetCountryById(billingAddress.CountryId ?? 0);
                var state = _stateProvinceService.GetStateProvinceById(billingAddress.StateProvinceId ?? 0);
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
            return _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext).RouteUrl(TwoCheckoutDefaults.ConfigurationRouteName);
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
            _localizationService.AddPluginLocaleResource(new Dictionary<string, string>
            {
                ["Plugins.Payments.2Checkout.AccountNumber"] = "Account number",
                ["Plugins.Payments.2Checkout.AccountNumber.Hint"] = "Enter account number.",
                ["Plugins.Payments.2Checkout.AdditionalFee"] = "Additional fee",
                ["Plugins.Payments.2Checkout.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
                ["Plugins.Payments.2Checkout.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payments.2Checkout.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.2Checkout.PaymentMethodDescription"] = "You will be redirected to 2Checkout site to complete the order.",
                ["Plugins.Payments.2Checkout.RedirectionTip"] = "You will be redirected to 2Checkout site to complete the order.",
                ["Plugins.Payments.2Checkout.SecretWord"] = "Secret Word",
                ["Plugins.Payments.2Checkout.SecretWord.Hint"] = "Enter secret word.",
                ["Plugins.Payments.2Checkout.UseSandbox"] = "Test mode",
                ["Plugins.Payments.2Checkout.UseSandbox.Hint"] = "Check to enable test orders."
            });

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
            _localizationService.DeletePluginLocaleResources("Plugins.Payments.2Checkout");

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