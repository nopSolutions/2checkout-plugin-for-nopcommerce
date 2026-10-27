using FluentMigrator;
using Nop.Data;
using Nop.Data.Migrations;
using Nop.Services.Localization;
using Nop.Web.Framework.Extensions;

namespace Nop.Plugin.Payments.TwoCheckout.Migrations;

[NopMigration("2025-08-25 00:00:00", "2Checkout. Update 4.80.2", MigrationProcessType.Update)]
public class Migration4802 : MigrationBase
{
    #region Fields

    protected readonly ILocalizationService _localizationService;

    #endregion

    #region Ctor

    public Migration4802(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    #endregion

    #region Methods

    public override void Up()
    {
        if (!DataSettingsManager.IsDatabaseInstalled())
            return;

        #region Locales

        this.AddOrUpdateLocaleResource(new Dictionary<string, string>
        {
            ["Plugins.Payments.2Checkout.SecretKey"] = "Secret Key",
            ["Plugins.Payments.2Checkout.SecretKey.Hint"] = "Enter secret key.",
            ["Plugins.Payments.2Checkout.SecretKey.Required"] = "Secret key is required",
        });

        #endregion

    }
    public override void Down()
    {
        //add the downgrade logic if necessary 
    }

    #endregion
}
