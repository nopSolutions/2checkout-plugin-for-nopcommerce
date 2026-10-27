using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.TwoCheckout.Controllers;

[Area(AreaNames.ADMIN)]
[AuthorizeAdmin]
[AutoValidateAntiforgeryToken]
public class PaymentTwoCheckoutController : BasePaymentController
{
    #region Fields

    private readonly ILocalizationService _localizationService;
    private readonly INotificationService _notificationService;
    private readonly ISettingService _settingService;
    private readonly IWebHelper _webHelper;
    private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

    #endregion

    #region Ctor

    public PaymentTwoCheckoutController(ILocalizationService localizationService,
        INotificationService notificationService,
        ISettingService settingService,
        IWebHelper webHelper,
        TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
    {
        _localizationService = localizationService;
        _notificationService = notificationService;
        _settingService = settingService;
        _webHelper = webHelper;
        _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public IActionResult Configure()
    {
        var requestProtocol = _webHelper.GetCurrentRequestProtocol();

        var model = new ConfigurationModel
        {
            AccountNumber = _twoCheckoutPaymentSettings.AccountNumber,
            SecretWord = _twoCheckoutPaymentSettings.SecretWord,
            SecretKey = _twoCheckoutPaymentSettings.SecretKey,
            UseSandbox = _twoCheckoutPaymentSettings.UseSandbox,
            AdditionalFee = _twoCheckoutPaymentSettings.AdditionalFee,
            AdditionalFeePercentage = _twoCheckoutPaymentSettings.AdditionalFeePercentage,
            IpnUrl = Url.RouteUrl(TwoCheckoutDefaults.IpnRouteName, null, requestProtocol),
            RedirectUrl = Url.RouteUrl(TwoCheckoutDefaults.CompletedRouteName, null, requestProtocol)
        };

        return View("~/Plugins/Payments.TwoCheckout/Views/Configure.cshtml", model);
    }

    [HttpPost]
    [CheckPermission(StandardPermission.Configuration.MANAGE_PAYMENT_METHODS)]
    public async Task<IActionResult> Configure(ConfigurationModel model)
    {
        if (!ModelState.IsValid)
            return Configure();

        _twoCheckoutPaymentSettings.AccountNumber = model.AccountNumber;
        _twoCheckoutPaymentSettings.SecretWord = model.SecretWord;
        _twoCheckoutPaymentSettings.SecretKey = model.SecretKey;
        _twoCheckoutPaymentSettings.UseSandbox = model.UseSandbox;
        _twoCheckoutPaymentSettings.AdditionalFee = model.AdditionalFee;
        _twoCheckoutPaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
        await _settingService.SaveSettingAsync(_twoCheckoutPaymentSettings);

        _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

        return Configure();
    }

    #endregion
}