using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Orders;

namespace Nop.Plugin.Payments.TwoCheckout.Services
{
    /// <summary>
    /// Represents payment manager implementation
    /// </summary>
    public class ConvertPlusManager : IPaymentManager
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly IAddressService _addressService;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly IOrderService _orderService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IWebHelper _webHelper;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public ConvertPlusManager(CurrencySettings currencySettings,
            IActionContextAccessor actionContextAccessor,
            IAddressService addressService,
            ICountryService countryService,
            ICurrencyService currencyService,
            IOrderService orderService,
            IStateProvinceService stateProvinceService,
            IUrlHelperFactory urlHelperFactory,
            IWebHelper webHelper,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _currencySettings = currencySettings;
            _actionContextAccessor = actionContextAccessor;
            _addressService = addressService;
            _countryService = countryService;
            _currencyService = currencyService;
            _orderService = orderService;
            _stateProvinceService = stateProvinceService;
            _urlHelperFactory = urlHelperFactory;
            _webHelper = webHelper;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Prepare billin address details
        /// </summary>
        /// <param name="addressId">Address identifier</param>
        /// <param name="builder">String builder</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task FillBillingAddressAsync(int addressId, StringBuilder builder)
        {
            var billingAddress = await _addressService.GetAddressByIdAsync(addressId);
            if (billingAddress is null)
                return;

            var country = await _countryService.GetCountryByIdAsync(billingAddress.CountryId ?? 0);
            var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);

            var firstName = billingAddress.FirstName;
            var lastName = billingAddress.LastName;

            var fullName = string.Empty;
            if (!string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName))
                fullName = $"{firstName} {lastName}";
            else
            {
                if (!string.IsNullOrWhiteSpace(firstName))
                    fullName = firstName;

                if (!string.IsNullOrWhiteSpace(lastName))
                    fullName = lastName;
            }

            builder.AppendFormat("&name={0}", WebUtility.UrlEncode(fullName));
            builder.AppendFormat("&address={0}", WebUtility.UrlEncode(billingAddress.Address1 ?? string.Empty));
            builder.AppendFormat("&city={0}", WebUtility.UrlEncode(billingAddress.City ?? string.Empty));
            builder.AppendFormat("&state={0}", WebUtility.UrlEncode(CommonHelper.EnsureMaximumLength(state?.Abbreviation, 2) ?? string.Empty));
            builder.AppendFormat("&country={0}", WebUtility.UrlEncode(country?.TwoLetterIsoCode ?? string.Empty));
            builder.AppendFormat("&zip={0}", WebUtility.UrlEncode(billingAddress.ZipPostalCode ?? string.Empty));
            builder.AppendFormat("&email={0}", WebUtility.UrlEncode(billingAddress.Email ?? string.Empty));
            builder.AppendFormat("&phone={0}", WebUtility.UrlEncode(CommonHelper.EnsureNumericOnly(billingAddress.PhoneNumber) ?? string.Empty));
        }

        /// <summary>
        /// Prepare order totals
        /// </summary>
        /// <param name="order">Order</param>
        /// <param name="builder">String builder</param>
        private static void FillOrderTotals(Order order, StringBuilder builder)
        {
            builder.AppendJoin(string.Empty,
                $"&prod={WebUtility.UrlEncode($"Order #{order.CustomOrderNumber}")}",
                "&qty=1",
                $"&price={order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture)}",
                "&type=digital");
        }

        /// <summary>
        /// Get signature by the parameters
        /// </summary>
        /// <param name="parameters">Parameters to sign</param>
        /// <returns>Signature</returns>
        private string GetSignature(Dictionary<string, StringValues> parameters)
        {
            var paramsToCache = string.Join(string.Empty, parameters.Where(p => !p.Key.Equals("signature")).OrderBy(p => p.Key).Select(p => $"{p.Value.ToString().Length}{p.Value}"));

            var sBuilder = new StringBuilder();

            var data = new HMACSHA256(Encoding.UTF8.GetBytes(_twoCheckoutPaymentSettings.SecretWord)).ComputeHash(Encoding.UTF8.GetBytes(paramsToCache));

            foreach (var theByte in data)
                sBuilder.Append(theByte.ToString("x2"));

            return sBuilder.ToString();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Handle return request
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the order id
        /// </returns>
        public async Task<int?> HandleReturnRequestAsync()
        {
            var request = _actionContextAccessor.ActionContext.HttpContext.Request;
            var form = request.HasFormContentType ? await request.ReadFormAsync() : new FormCollection(null);
            var parameters = request.HasFormContentType && form.Keys.Any()
                ? form.ToDictionary(param => param.Key.ToLower(), param => param.Value)
                : request.Query.ToDictionary(param => param.Key.ToLower(), param => param.Value);

            //define local function to get a value from the request Form or from the Query parameters
            string getValue(string key) => parameters.TryGetValue(key.ToLower(), out var value) ? value.ToString() : string.Empty;

            //try to get an order by the number
            var customOrderNumber = getValue("order-ext-ref");
            var order = await _orderService.GetOrderByCustomOrderNumberAsync(customOrderNumber)
                ?? throw new NopException($"Order '{customOrderNumber}' not found");

            //save request info as order note for debug purposes
            var note = parameters.Aggregate("ConvertPlus redirect",
                (text, param) => $"{text}{Environment.NewLine}{param.Key}: {param.Value}");
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = note,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            string computedSignature;
            string receivedSignature;

            bool isSignatureValid()
            {
                computedSignature = GetSignature(parameters);
                receivedSignature = getValue("signature");

                return string.Equals(computedSignature, receivedSignature, StringComparison.InvariantCultureIgnoreCase);
            }

            //verify the passed data by comparing signature
            if (!isSignatureValid())
            {
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = $"Computed signature '{computedSignature}' is not equal to received signature '{receivedSignature}'",
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });
            }

            return order.Id;
        }

        /// <summary>
        /// Create the redirect URL
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the redirect URL
        /// </returns>
        public async Task<string> GetRequestUrlAsync(Order order)
        {
            var builder = new StringBuilder($"{TwoCheckoutDefaults.ServiceUrl.ConvertPlus}?dynamic=1");

            var currency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

            builder.AppendFormat("&merchant={0}", _twoCheckoutPaymentSettings.AccountNumber);
            builder.AppendFormat("&currency={0}", currency?.CurrencyCode);
            builder.AppendFormat("&order-ext-ref={0}", order.CustomOrderNumber);

            var urlHelper = _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);
            var url = urlHelper.RouteUrl(TwoCheckoutDefaults.CompletedRouteName, null, _webHelper.GetCurrentRequestProtocol());
            builder.AppendFormat("&return-url={0}", url);
            builder.Append("&return-type=redirect");

            if (_twoCheckoutPaymentSettings.UseSandbox)
                builder.AppendFormat("&test=1");

            FillOrderTotals(order, builder);

            await FillBillingAddressAsync(order.BillingAddressId, builder);

            var queryString = QueryString.FromUriComponent($"?{builder.ToString().Split("?")[1]}").Value;

            var parameters = new[]
            {
                "return-url",
                "return-type",
                "expiration",
                "order-ext-ref",
                "customer-ref",
                "customer-ext-ref",
                "currency",
                "prod",
                "price",
                "qty",
                "type",
                "opt",
                "description",
                "recurrence",
                "duration",
                "renewal-price",
                "item-ext-ref"
            };

            var dataToSign = QueryHelpers.ParseQuery(queryString)
                .Where(p => parameters.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            builder.AppendFormat("&signature={0}", GetSignature(dataToSign));

            return builder.ToString();
        }

        #endregion
    }
}