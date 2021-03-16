using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Logging;
using Nop.Services.Payments;

namespace Nop.Plugin.Payments.TwoCheckout.Controllers
{
    public class PaymentTwoCheckoutIpnController : Controller
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IStoreContext _storeContext;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public PaymentTwoCheckoutIpnController(ILogger logger,
            IPaymentPluginManager paymentPluginManager,
            IStoreContext storeContext,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _logger = logger;
            _paymentPluginManager = paymentPluginManager;
            _storeContext = storeContext;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Methods

        public async Task<IActionResult> IPNHandler()
        {
            try
            {
                //ensure this payment method is active
                var store = await _storeContext.GetCurrentStoreAsync();
                var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(TwoCheckoutDefaults.SystemName, storeId: store.Id);
                if (!_paymentPluginManager.IsPluginActive(paymentMethod) || paymentMethod is not TwoCheckoutPaymentProcessor plugin)
                    throw new NopException($"{TwoCheckoutDefaults.SystemName} error. Module cannot be loaded");

                await plugin.HandleTransactionAsync(true);
            }
            catch (Exception exception)
            {
                if (_twoCheckoutPaymentSettings.LogIpnErrors)
                    await _logger.ErrorAsync(exception.Message, exception);
            }

            return Ok();
        }

        #endregion
    }
}