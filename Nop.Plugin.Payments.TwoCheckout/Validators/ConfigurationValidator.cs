using FluentValidation;
using Nop.Plugin.Payments.TwoCheckout.Models;
using Nop.Services.Localization;
using Nop.Web.Framework.Validators;

namespace Nop.Plugin.Payments.TwoCheckout.Validators
{
    /// <summary>
    /// Represents configuration model validator
    /// </summary>
    public class ConfigurationValidator : BaseNopValidator<ConfigurationModel>
    {
        #region Ctor

        public ConfigurationValidator(ILocalizationService localizationService)
        {
            RuleFor(model => model.AccountNumber)
                .NotEmpty()
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Payments.2Checkout.AccountNumber.Required"))
                .When(model => !model.UseSandbox);

            RuleFor(model => model.SecretWord)
                .NotEmpty()
                .WithMessageAwait(localizationService.GetResourceAsync("Plugins.Payments.2Checkout.SecretWord.Required"))
                .When(model => !model.UseSandbox);
        }

        #endregion
    }
}