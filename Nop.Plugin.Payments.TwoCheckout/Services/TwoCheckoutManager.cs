using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Html;
using Nop.Services.Orders;

namespace Nop.Plugin.Payments.TwoCheckout.Services
{
    /// <summary>
    /// Represents payment manager implementation
    /// </summary>
    public class TwoCheckoutManager : IPaymentManager
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly IActionContextAccessor _actionContextAccessor;
        private readonly IAddressService _addressService;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly IHtmlFormatter _htmlFormatter;
        private readonly IOrderService _orderService;
        private readonly IProductService _productService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

        #endregion

        #region Ctor

        public TwoCheckoutManager(CurrencySettings currencySettings,
            IActionContextAccessor actionContextAccessor,
            IAddressService addressService,
            ICountryService countryService,
            ICurrencyService currencyService,
            IHtmlFormatter htmlFormatter,
            IOrderService orderService,
            IProductService productService,
            IStateProvinceService stateProvinceService,
            TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
        {
            _currencySettings = currencySettings;
            _actionContextAccessor = actionContextAccessor;
            _addressService = addressService;
            _countryService = countryService;
            _currencyService = currencyService;
            _htmlFormatter = htmlFormatter;
            _orderService = orderService;
            _productService = productService;
            _stateProvinceService = stateProvinceService;
            _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
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

            //try to get an order by the number or by the identifier (used in previous plugin versions)
            var customOrderNumber = getValue("x_invoice_num");

            var order = await _orderService.GetOrderByCustomOrderNumberAsync(customOrderNumber)
                ?? await _orderService.GetOrderByIdAsync(int.TryParse(customOrderNumber, out var orderId) ? orderId : 0)
                ?? throw new NopException($"Order '{customOrderNumber}' not found");

            //save request info as order note for debug purposes
            var note = parameters.Aggregate("2Checkout redirect:", (text, param) => $"{text}{Environment.NewLine}{param.Key}: {param.Value}");
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = note,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

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
            var builder = new StringBuilder();

            builder.AppendFormat("{0}?id_type=1", TwoCheckoutDefaults.ServiceUrl.TwoCheckout);

            //products
            var orderProducts = await _orderService.GetOrderItemsAsync(order.Id);
            for (var i = 0; i < orderProducts.Count; i++)
            {
                var pNum = i + 1;
                var orderItem = orderProducts[i];
                var product = await _productService.GetProductByIdAsync(orderProducts[i].ProductId);

                var cProd = $"c_prod_{pNum}";
                var cProdValue = $"{product.Sku},{orderItem.Quantity}";
                builder.AppendFormat("&{0}={1}", cProd, cProdValue);

                var cName = $"c_name_{pNum}";
                var cNameValue = product.Name;
                builder.AppendFormat("&{0}={1}", WebUtility.UrlEncode(cName), WebUtility.UrlEncode(cNameValue));

                var cDescription = $"c_description_{pNum}";
                var cDescriptionValue = cNameValue;
                if (!string.IsNullOrEmpty(orderItem.AttributeDescription))
                    cDescriptionValue = _htmlFormatter.StripTags($"{cDescriptionValue}. {orderItem.AttributeDescription}");
                builder.AppendFormat("&{0}={1}", WebUtility.UrlEncode(cDescription), WebUtility.UrlEncode(cDescriptionValue));

                var cPrice = $"c_price_{pNum}";
                var cPriceValue = orderItem.UnitPriceInclTax.ToString("0.00", CultureInfo.InvariantCulture);
                builder.AppendFormat("&{0}={1}", cPrice, cPriceValue);

                var cTangible = $"c_tangible_{pNum}";
                var cTangibleValue = product.IsDownload ? "N" : "Y";
                builder.AppendFormat("&{0}={1}", cTangible, cTangibleValue);
            }

            builder.AppendFormat("&x_login={0}", _twoCheckoutPaymentSettings.AccountNumber);
            builder.AppendFormat("&sid={0}", _twoCheckoutPaymentSettings.AccountNumber);
            builder.AppendFormat("&x_amount={0}", order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));
            var currency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);
            builder.AppendFormat("&currency_code={0}", currency?.CurrencyCode);
            builder.AppendFormat("&x_invoice_num={0}", order.CustomOrderNumber);

            if (_twoCheckoutPaymentSettings.UseSandbox)
                builder.AppendFormat("&demo=Y");

            var billingAddress = await _addressService.GetAddressByIdAsync(order.BillingAddressId);
            if (billingAddress != null)
            {
                var country = await _countryService.GetCountryByIdAsync(billingAddress.CountryId ?? 0);
                var state = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);
                builder.AppendFormat("&x_First_Name={0}", WebUtility.UrlEncode(billingAddress.FirstName ?? string.Empty));
                builder.AppendFormat("&x_Last_Name={0}", WebUtility.UrlEncode(billingAddress.LastName ?? string.Empty));
                builder.AppendFormat("&x_Address={0}", WebUtility.UrlEncode(billingAddress.Address1 ?? string.Empty));
                builder.AppendFormat("&x_City={0}", WebUtility.UrlEncode(billingAddress.City ?? string.Empty));
                builder.AppendFormat("&x_State={0}", WebUtility.UrlEncode(state?.Abbreviation ?? string.Empty));
                builder.AppendFormat("&x_Country={0}", WebUtility.UrlEncode(country?.ThreeLetterIsoCode ?? string.Empty));
                builder.AppendFormat("&x_Zip={0}", WebUtility.UrlEncode(billingAddress.ZipPostalCode ?? string.Empty));
                builder.AppendFormat("&x_EMail={0}", WebUtility.UrlEncode(billingAddress.Email ?? string.Empty));
                builder.AppendFormat("&x_Phone={0}", WebUtility.UrlEncode(CommonHelper.EnsureNumericOnly(billingAddress.PhoneNumber) ?? string.Empty));
            }

            return builder.ToString();
        }

        #endregion
    }
}