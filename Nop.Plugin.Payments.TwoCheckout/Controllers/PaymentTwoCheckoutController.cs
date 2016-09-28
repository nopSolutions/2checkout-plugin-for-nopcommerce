using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework.Controllers;

namespace Nop.Plugin.Payments.TwoCheckout.Controllers
{
    public class PaymentTwoCheckoutController : BasePaymentController
    {
        #region Fields

        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;
        private readonly PaymentSettings _paymentSettings;
        private readonly ILocalizationService _localizationService;
        private readonly IWebHelper _webHelper;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutController(ISettingService settingService,
            IPaymentService paymentService, 
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings,
            ILocalizationService localizationService,
            IWebHelper webHelper,
            PaymentSettings paymentSettings)
        {
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
            this._localizationService = localizationService;
            this._webHelper = webHelper;
            this._paymentSettings = paymentSettings;
        }

        #endregion

        #region Methods

        private string GetValue(FormCollection form, string key)
        {
            return form.AllKeys.Any(k => k == key) ? form[key] : _webHelper.QueryString<string>(key);
        }

        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure()
        {
            var model = new ConfigurationModel
            {
                UseSandbox = _twoCheckoutPaymentSettings.UseSandbox,
                AccountNumber = _twoCheckoutPaymentSettings.AccountNumber,
                UseMd5Hashing = _twoCheckoutPaymentSettings.UseMd5Hashing,
                SecretWord = _twoCheckoutPaymentSettings.SecretWord,
                AdditionalFee = _twoCheckoutPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _twoCheckoutPaymentSettings.AdditionalFeePercentage
            };

            return View("~/Plugins/Payments.TwoCheckout/Views/PaymentTwoCheckout/Configure.cshtml", model);
        }

        [HttpPost]
        [AdminAuthorize]
        [ChildActionOnly]
        public ActionResult Configure(ConfigurationModel model)
        {
            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _twoCheckoutPaymentSettings.UseSandbox = model.UseSandbox;
            _twoCheckoutPaymentSettings.AccountNumber = model.AccountNumber;
            _twoCheckoutPaymentSettings.UseMd5Hashing = model.UseMd5Hashing;
            _twoCheckoutPaymentSettings.SecretWord = model.SecretWord;
            _twoCheckoutPaymentSettings.AdditionalFee = model.AdditionalFee;
            _twoCheckoutPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            _settingService.SaveSetting(_twoCheckoutPaymentSettings);

            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return View("~/Plugins/Payments.TwoCheckout/Views/PaymentTwoCheckout/Configure.cshtml", model);
        }

        [ChildActionOnly]
        public ActionResult PaymentInfo()
        {
            return View("~/Plugins/Payments.TwoCheckout/Views/PaymentTwoCheckout/PaymentInfo.cshtml");
        }

        [NonAction]
        public override IList<string> ValidatePaymentForm(FormCollection form)
        {
            var warnings = new List<string>();

            return warnings;
        }

        [NonAction]
        public override ProcessPaymentRequest GetPaymentInfo(FormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();

            return paymentInfo;
        }

        [ValidateInput(false)]
        public ActionResult IPNHandler(FormCollection form)
        {
            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.TwoCheckout") as TwoCheckoutPaymentProcessor;

            if (processor == null ||
                !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
                throw new NopException("TwoCheckout module cannot be loaded");

            //x_invoice_num
            var nopOrderIdStr = GetValue(form, "x_invoice_num");
            int nopOrderId;
            int.TryParse(nopOrderIdStr, out nopOrderId);
            var order = _orderService.GetOrderById(nopOrderId);

            if (order == null)
            {
                return RedirectToAction("Index", "Home", new { area = "" });
            }

            //debug info
            var sbDebug = new StringBuilder();
            sbDebug.AppendLine("2Checkout IPN:");

            foreach (var key in form.AllKeys)
            {
                var value = form[key];

                sbDebug.AppendLine(key + ": " + value);
            }

            if(!form.HasKeys())
                sbDebug.AppendLine("url: " + _webHelper.GetThisPageUrl(true));

            order.OrderNotes.Add(new OrderNote
            {
                Note = sbDebug.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            _orderService.UpdateOrder(order);
           
            //invoice id
            var invoice_id = GetValue(form, "invoice_id") ?? string.Empty;

            //order number
            var orderNum = _twoCheckoutPaymentSettings.UseSandbox ? "1" : GetValue(form, "order_number");

            if (_twoCheckoutPaymentSettings.UseMd5Hashing)
            {
                var vendorId = _twoCheckoutPaymentSettings.AccountNumber;
                var secretWord = _twoCheckoutPaymentSettings.SecretWord;
                var compareHash1 = processor.CalculateMD5hash(secretWord + vendorId + orderNum + order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

                if (string.IsNullOrEmpty(compareHash1))
                    throw new NopException("2Checkout empty hash string");

                var compareHash2 = GetValue(form, "x_md5_hash") ?? string.Empty;
                
                if (compareHash1.ToUpperInvariant() != compareHash2.ToUpperInvariant())
                {
                    order.OrderNotes.Add(new OrderNote()
                    {
                        Note = "Hash validation failed",
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });

                    _orderService.UpdateOrder(order);

                    return RedirectToRoute("OrderDetails", new {orderId = order.Id});
                }
            }

            var message_type = GetValue(form, "message_type") ?? string.Empty;
            var invoice_status = GetValue(form, "invoice_status") ?? string.Empty;
            var fraud_status = GetValue(form, "fraud_status") ?? string.Empty;
            var payment_type = GetValue(form, "payment_type") ?? string.Empty;

            var newPaymentStatus = PaymentStatus.Pending;

            if (message_type.ToUpperInvariant() == "FRAUD_STATUS_CHANGED"
               && fraud_status == "pass"
               && (invoice_status == "approved" || payment_type == "paypal ec"))
            {
                newPaymentStatus = PaymentStatus.Paid;
            }

            //from documentation (https://www.2checkout.com/documentation/checkout/return):
            //if your return method is set to Direct Return or Header Redirect, 
            //the buyer gets directed to automatically after the successful sale.
            if (message_type + invoice_status + fraud_status + payment_type == string.Empty)
                newPaymentStatus = PaymentStatus.Paid;

            var sb = new StringBuilder();
            sb.AppendLine("2Checkout IPN:");
            sb.AppendLine("order_number: " + orderNum);
            sb.AppendLine("invoice_id: " + invoice_id);
            sb.AppendLine("message_type: " + message_type);
            sb.AppendLine("invoice_status: " + invoice_status);
            sb.AppendLine("fraud_status: " + fraud_status);
            sb.AppendLine("payment_type: " + payment_type);
            sb.AppendLine("New payment status: " + newPaymentStatus);

            order.OrderNotes.Add(new OrderNote
            {
                Note = sb.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            _orderService.UpdateOrder(order);

            if (newPaymentStatus == PaymentStatus.Paid && _orderProcessingService.CanMarkOrderAsPaid(order))
            {
                _orderProcessingService.MarkOrderAsPaid(order);
                return RedirectToRoute("CheckoutCompleted", new {orderId = order.Id});
            }

            return RedirectToRoute("OrderDetails", new {orderId = order.Id});
        }

        #endregion
    }
}