using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
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

        private readonly ILocalizationService _localizationService;
        private readonly INotificationService _notificationService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutController(ILocalizationService localizationService,
            INotificationService notificationService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _localizationService = localizationService;
            _notificationService = notificationService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                AccountNumber = _twoCheckoutPaymentSettings.AccountNumber,
                SecretWord = _twoCheckoutPaymentSettings.SecretWord,
                UseSandbox = _twoCheckoutPaymentSettings.UseSandbox,
                AdditionalFee = _twoCheckoutPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _twoCheckoutPaymentSettings.AdditionalFeePercentage
            };

            return View("~/Plugins/Payments.TwoCheckout/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [AdminAntiForgery]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _twoCheckoutPaymentSettings.AccountNumber = model.AccountNumber;
            _twoCheckoutPaymentSettings.SecretWord = model.SecretWord;
            _twoCheckoutPaymentSettings.UseSandbox = model.UseSandbox;
            _twoCheckoutPaymentSettings.AdditionalFee = model.AdditionalFee;
            _twoCheckoutPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            _settingService.SaveSetting(_twoCheckoutPaymentSettings);

            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));

            return Configure();
        }

        public IActionResult IPNHandler()
        {
            //ensure this payment method is active
            if (!_paymentPluginManager.IsPluginActive(TwoCheckoutDefaults.SystemName,
                _workContext.CurrentCustomer, _storeContext.CurrentStore.Id))
            {
                throw new NopException("2Checkout module cannot be loaded");
            }

            //define local function to get a value from the request Form or from the Query parameters
            string getValue(string key) =>
                Request.HasFormContentType && Request.Form.TryGetValue(key, out var value)
                ? value.ToString()
                : _webHelper.QueryString<string>(key)
                ?? string.Empty;

            //get order
            var customOrderNumber = getValue("x_invoice_num");
            var order = _orderService.GetOrderByCustomOrderNumber(customOrderNumber);
            if (order == null)
            {
                //try to get order by the order identifier (used in previous plugin versions)
                int.TryParse(customOrderNumber, out var orderId);
                order = _orderService.GetOrderById(orderId);
                if (order == null)
                    return RedirectToRoute("Homepage");
            }

            //save request info as order note for debug purposes
            var info = new StringBuilder();
            info.AppendLine("2Checkout IPN:");
            if (Request.HasFormContentType && Request.Form.Keys.Any())
            {
                //form parameters
                foreach (var key in Request.Form.Keys)
                {
                    info.AppendLine($"{key}: {Request.Form[key]}");
                }
            }
            else
            {
                //query parameters
                info.AppendLine(Request.QueryString.ToString());
            }
            order.OrderNotes.Add(new OrderNote
            {
                Note = info.ToString(),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });
            _orderService.UpdateOrder(order);

            //verify the passed data by comparing MD5 hashes
            if (_twoCheckoutPaymentSettings.UseMd5Hashing)
            {
                var stringToHash = $"{_twoCheckoutPaymentSettings.SecretWord}" +
                    $"{_twoCheckoutPaymentSettings.AccountNumber}" +
                    $"{(_twoCheckoutPaymentSettings.UseSandbox ? "1" : getValue("order_number"))}" +
                    $"{getValue("x_amount")}";
                var data = new MD5CryptoServiceProvider().ComputeHash(Encoding.Default.GetBytes(stringToHash));
                var sBuilder = new StringBuilder();
                foreach (var t in data)
                {
                    sBuilder.Append(t.ToString("x2"));
                }

                var computedHash = sBuilder.ToString();
                var receivedHash = getValue("x_md5_hash");

                if (computedHash.ToUpperInvariant() != receivedHash.ToUpperInvariant())
                {
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = "Hash validation failed",
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);

                    return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                }
            }

            var newPaymentStatus = PaymentStatus.Pending;

            var messageType = getValue("message_type");
            var invoiceStatus = getValue("invoice_status");
            var fraudStatus = getValue("fraud_status");
            var paymentType = getValue("payment_type");

            //from documentation (https://www.2checkout.com/documentation/checkout/return):
            //if your return method is set to Direct Return or Header Redirect, 
            //the buyer gets directed to automatically after the successful sale.
            if (messageType + invoiceStatus + fraudStatus + paymentType == string.Empty)
                newPaymentStatus = PaymentStatus.Paid;

            if (messageType.ToUpperInvariant() == "FRAUD_STATUS_CHANGED"
               && fraudStatus == "pass"
               && (invoiceStatus == "approved" || invoiceStatus == "deposited" || paymentType == "paypal ec"))
            {
                newPaymentStatus = PaymentStatus.Paid;
            }

            if (newPaymentStatus != PaymentStatus.Paid || !_orderProcessingService.CanMarkOrderAsPaid(order))
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            //mark order as paid
            _orderProcessingService.MarkOrderAsPaid(order);
            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }

        #endregion
    }
}