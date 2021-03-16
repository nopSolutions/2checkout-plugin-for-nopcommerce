using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
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

        [Area(AreaNames.Admin)]
        [AuthorizeAdmin]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                AccountNumber = _twoCheckoutPaymentSettings.AccountNumber,
                SecretWord = _twoCheckoutPaymentSettings.SecretWord,
                UseSandbox = _twoCheckoutPaymentSettings.UseSandbox,
                AdditionalFee = _twoCheckoutPaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _twoCheckoutPaymentSettings.AdditionalFeePercentage
            };

            model.IpnUrl = Url.RouteUrl(TwoCheckoutDefaults.IpnRouteName, null, _webHelper.GetCurrentRequestProtocol());
            model.RedirectUrl = Url.RouteUrl(TwoCheckoutDefaults.CompletedRouteName, null, _webHelper.GetCurrentRequestProtocol());

            return View("~/Plugins/Payments.TwoCheckout/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [Area(AreaNames.Admin)]
        [AuthorizeAdmin]
        [AutoValidateAntiforgeryToken]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            _twoCheckoutPaymentSettings.AccountNumber = model.AccountNumber;
            _twoCheckoutPaymentSettings.SecretWord = model.SecretWord;
            _twoCheckoutPaymentSettings.UseSandbox = model.UseSandbox;
            _twoCheckoutPaymentSettings.AdditionalFee = model.AdditionalFee;
            _twoCheckoutPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            await _settingService.SaveSettingAsync(_twoCheckoutPaymentSettings);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }

        [CheckAccessPublicStore]
        public async Task<IActionResult> Completed()
        {
            try
            {
                var customer = await _workContext.GetCurrentCustomerAsync();
                var store = await _storeContext.GetCurrentStoreAsync();
                var paymentMethod = await _paymentPluginManager.LoadPluginBySystemNameAsync(TwoCheckoutDefaults.SystemName, customer, store.Id);
                if (!_paymentPluginManager.IsPluginActive(paymentMethod) || paymentMethod is not TwoCheckoutPaymentProcessor plugin)
                    throw new NopException($"{TwoCheckoutDefaults.SystemName} error. Module cannot be loaded");

                var orderId = await plugin.HandleTransactionAsync(false);
                if (!orderId.HasValue)
                    throw new NopException($"{TwoCheckoutDefaults.SystemName} error. Order not found");

                return RedirectToRoute("CheckoutCompleted", new { orderId = orderId.Value });
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