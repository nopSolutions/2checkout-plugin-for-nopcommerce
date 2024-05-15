using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.TwoCheckout.Services;
using Nop.Services.Messages;
using Nop.Services.Payments;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.TwoCheckout.Controllers
{
    [WwwRequirement]
    [CheckLanguageSeoCode]
    [CheckAccessPublicStore]
    [CheckAccessClosedStore]
    public class PaymentTwoCheckoutPublicController : BasePaymentController
    {
        #region Fields

        private readonly INotificationService _notificationService;
        private readonly IPaymentManager _paymentManager;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutPublicController(INotificationService notificationService,
            IPaymentManager paymentManager,
            IPaymentPluginManager paymentPluginManager,
            IStoreContext storeContext,
            IWorkContext workContext)
        {
            _notificationService = notificationService;
            _paymentManager = paymentManager;
            _paymentPluginManager = paymentPluginManager;
            _storeContext = storeContext;
            _workContext = workContext;
        }

        #endregion

        #region Methods

        public async Task<IActionResult> Completed()
        {
            try
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                var store = await _storeContext.GetCurrentStoreAsync();
                var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(TwoCheckoutDefaults.SystemName, customer, store.Id);
                if (!_paymentPluginManager.IsPluginActive(paymentMethod))
                    throw new NopException($"{TwoCheckoutDefaults.SystemName} error. Module cannot be loaded");

                var orderId = await _paymentManager.HandleReturnRequestAsync()
                    ?? throw new NopException($"{TwoCheckoutDefaults.SystemName} error. Order not found");

                return RedirectToRoute("CheckoutCompleted", new { orderId = orderId });
            }
            catch (Exception exception)
            {
                await _notificationService.ErrorNotificationAsync(exception);
                return RedirectToRoute("Homepage");
            }
        }

        #endregion
    }
}