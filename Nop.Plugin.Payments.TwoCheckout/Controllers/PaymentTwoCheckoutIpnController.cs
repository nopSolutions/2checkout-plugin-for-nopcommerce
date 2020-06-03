using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.TwoCheckout.Controllers
{
    [WwwRequirement]
    [CheckAccessPublicStore]
    [CheckAccessClosedStore]
    [CheckLanguageSeoCode]
    [CheckDiscountCoupon]
    [CheckAffiliate]
    public class PaymentTwoCheckoutIpnController : BasePaymentController
    {
        #region Fields

        private readonly INotificationService _notificationService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutIpnController(INotificationService notificationService,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _notificationService = notificationService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Methods

        public IActionResult IPNHandler()
        {
            try
            {
                //define local function to get a value from the request Form or from the Query parameters
                string getValue(string key) =>
                    Request.HasFormContentType && Request.Form.TryGetValue(key, out var value)
                    ? value.ToString()
                    : _webHelper.QueryString<string>(key)
                    ?? string.Empty;

                //ensure this payment method is active
                if (!_paymentPluginManager.IsPluginActive(TwoCheckoutDefaults.SystemName,
                    _workContext.CurrentCustomer, _storeContext.CurrentStore.Id))
                {
                    throw new NopException("2Checkout module cannot be loaded");
                }

                //try to get an order by the number or by the identifier (used in previous plugin versions)
                var customOrderNumber = getValue("x_invoice_num");
                var order = _orderService.GetOrderByCustomOrderNumber(customOrderNumber)
                    ?? _orderService.GetOrderById(int.TryParse(customOrderNumber, out var orderId) ? orderId : 0)
                    ?? throw new NopException($"2Checkout plugin error. Order {customOrderNumber} not found");

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
                _orderService.InsertOrderNote(new OrderNote
                {
                    OrderId = order.Id,
                    Note = info.ToString(),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

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
                        _orderService.InsertOrderNote(new OrderNote
                        {
                            OrderId = order.Id,
                            Note = "Hash validation failed",
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow
                        });

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
            catch (Exception exception)
            {
                _notificationService.ErrorNotification(exception);
                return RedirectToRoute("Homepage");
            }
        }

        #endregion
    }
}