using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

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
        private readonly IPermissionService _permissionService;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutController(ISettingService settingService,
            IPaymentService paymentService, 
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings,
            ILocalizationService localizationService,
            IWebHelper webHelper,
            PaymentSettings paymentSettings,
            IPermissionService permissionService)
        {
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
            this._localizationService = localizationService;
            this._webHelper = webHelper;
            this._paymentSettings = paymentSettings;
            this._permissionService = permissionService;
        }

        #endregion

        #region Methods

        private string GetValue(IFormCollection form, string key)
        {
            return form.Keys.Any(k => k == key) ? form[key].ToString() : _webHelper.QueryString<string>(key);
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                UseSandbox = _twoCheckoutPaymentSettings.UseSandbox,
                AccountNumber = _twoCheckoutPaymentSettings.AccountNumber,
                UseMd5Hashing = _twoCheckoutPaymentSettings.UseMd5Hashing,
                SecretWord = _twoCheckoutPaymentSettings.SecretWord,
                AdditionalFee = _twoCheckoutPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _twoCheckoutPaymentSettings.AdditionalFeePercentage
            };

            return View("~/Plugins/Payments.TwoCheckout/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

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

            return View("~/Plugins/Payments.TwoCheckout/Views/Configure.cshtml", model);
        }
       
        public IActionResult IPNHandler()
        {
            var form = Request.Form;
            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.TwoCheckout") as TwoCheckoutPaymentProcessor;

            if (processor == null ||
                !processor.IsPaymentMethodActive(_paymentSettings) || !processor.PluginDescriptor.Installed)
                throw new NopException("TwoCheckout module cannot be loaded");

            //x_invoice_num
            var nopOrderIdStr = GetValue(form, "x_invoice_num");
            int.TryParse(nopOrderIdStr, out int nopOrderId);
            var order = _orderService.GetOrderById(nopOrderId);

            if (order == null)
            {
                return RedirectToAction("Index", "Home", new { area = "" });
            }

            //debug info
            var sbDebug = new StringBuilder();
            sbDebug.AppendLine("2Checkout IPN:");

            foreach (var key in form.Keys)
            {
                var value = form[key];

                sbDebug.AppendLine(key + ": " + value);
            }

            if(!form.Keys.Any())
                sbDebug.AppendLine("url: " + _webHelper.GetThisPageUrl(true));

            order.OrderNotes.Add(new OrderNote
            {
                Note = sbDebug.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            _orderService.UpdateOrder(order);
           
            //invoice id
            var invoiceID = GetValue(form, "invoice_id") ?? string.Empty;

            //order number
            var orderNum = _twoCheckoutPaymentSettings.UseSandbox ? "1" : GetValue(form, "order_number");

            if (_twoCheckoutPaymentSettings.UseMd5Hashing)
            {
                var vendorId = _twoCheckoutPaymentSettings.AccountNumber;
                var secretWord = _twoCheckoutPaymentSettings.SecretWord;
                var compareHash1 = processor.CalculateMD5Hash(secretWord + vendorId + orderNum + order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

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

            var messageType = GetValue(form, "message_type") ?? string.Empty;
            var invoiceStatus = GetValue(form, "invoice_status") ?? string.Empty;
            var fraudStatus = GetValue(form, "fraud_status") ?? string.Empty;
            var paymentType = GetValue(form, "payment_type") ?? string.Empty;

            var newPaymentStatus = PaymentStatus.Pending;

            if (messageType.ToUpperInvariant() == "FRAUD_STATUS_CHANGED"
               && fraudStatus == "pass"
               && (invoiceStatus == "approved" || paymentType == "paypal ec"))
            {
                newPaymentStatus = PaymentStatus.Paid;
            }

            //from documentation (https://www.2checkout.com/documentation/checkout/return):
            //if your return method is set to Direct Return or Header Redirect, 
            //the buyer gets directed to automatically after the successful sale.
            if (messageType + invoiceStatus + fraudStatus + paymentType == string.Empty)
                newPaymentStatus = PaymentStatus.Paid;

            var sb = new StringBuilder();
            sb.AppendLine("2Checkout IPN:");
            sb.AppendLine("order_number: " + orderNum);
            sb.AppendLine("invoice_id: " + invoiceID);
            sb.AppendLine("message_type: " + messageType);
            sb.AppendLine("invoice_status: " + invoiceStatus);
            sb.AppendLine("fraud_status: " + fraudStatus);
            sb.AppendLine("payment_type: " + paymentType);
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