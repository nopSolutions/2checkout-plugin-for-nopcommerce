﻿using System.ComponentModel.DataAnnotations;
using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.TwoCheckout.Models
{
    /// <summary>
    /// Represents configuration model
    /// </summary>
    public record ConfigurationModel : BaseNopModel
    {
        #region Properties

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AccountNumber")]
        public string AccountNumber { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.SecretWord")]
        [DataType(DataType.Password)]
        public string SecretWord { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.UseSandbox")]
        public bool UseSandbox { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AdditionalFee")]
        public decimal AdditionalFee { get; set; }

        [NopResourceDisplayName("Plugins.Payments.2Checkout.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }

        public string IpnUrl { get; set; }

        public string RedirectUrl { get; set; }

        #endregion
    }
}