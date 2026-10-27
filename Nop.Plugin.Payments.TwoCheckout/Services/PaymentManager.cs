using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Common;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Orders;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.TwoCheckout.Services;

/// <summary>
/// Represents payment manager
/// </summary>
public class PaymentManager
{
    #region Fields

    private readonly CurrencySettings _currencySettings;
    private readonly IAddressService _addressService;
    private readonly ICountryService _countryService;
    private readonly ICurrencyService _currencyService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INopUrlHelper _nopUrlHelper;
    private readonly IOrderProcessingService _orderProcessingService;
    private readonly IOrderService _orderService;
    private readonly IStateProvinceService _stateProvinceService;
    private readonly IWebHelper _webHelper;
    private readonly TwoCheckoutPaymentSettings _twoCheckoutPaymentSettings;

    #endregion

    #region Ctor

    public PaymentManager(CurrencySettings currencySettings,
        IAddressService addressService,
        ICountryService countryService,
        ICurrencyService currencyService,
        IHttpContextAccessor httpContextAccessor,
        INopUrlHelper nopUrlHelper,
        IOrderProcessingService orderProcessingService,
        IOrderService orderService,
        IStateProvinceService stateProvinceService,
        IWebHelper webHelper,
        TwoCheckoutPaymentSettings twoCheckoutPaymentSettings)
    {
        _currencySettings = currencySettings;
        _addressService = addressService;
        _countryService = countryService;
        _httpContextAccessor = httpContextAccessor;
        _currencyService = currencyService;
        _nopUrlHelper = nopUrlHelper;
        _orderProcessingService = orderProcessingService;
        _orderService = orderService;
        _stateProvinceService = stateProvinceService;
        _webHelper = webHelper;
        _twoCheckoutPaymentSettings = twoCheckoutPaymentSettings;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Verify the passed data by comparing hashes
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    private async Task VerifyPassedDataAsync(int orderId, Func<string, string> getValue)
    {
        if (!(_twoCheckoutPaymentSettings.UseHashing ?? true))
            return;

        //More details: https://verifone.cloud/docs/2checkout/API-Integration/Webhooks/Instant-Notification-Service-%2528INS%2529/INS-read-receipt-response#validate-response

        var dataToHash = Encoding.Default.GetBytes($"{getValue("sale_id")}{_twoCheckoutPaymentSettings.AccountNumber}{getValue("invoice_id")}{_twoCheckoutPaymentSettings.SecretWord}");

        var receivedHash = getValue("hash");

        var hashArray = receivedHash.Split(':');
        var algorithm = hashArray.Length > 1 ? hashArray[0].Replace('-', '_').Trim() : "sha256";

        if (hashArray.Length > 1)
            receivedHash = hashArray[1].Trim();

        var key = Encoding.Default.GetBytes(_twoCheckoutPaymentSettings.SecretKey);

        var data = algorithm?.ToLower() switch
        {
            "sha256" => new HMACSHA256(key).ComputeHash(dataToHash),
            "sha3_256" => new HMACSHA3_256(key).ComputeHash(dataToHash),
            _ => throw new NopException($"Hash algorithm '{algorithm}' is not supported")
        };

        var sBuilder = new StringBuilder();

        foreach (var t in data)
            sBuilder.AppendFormat("{0:x2}", t);

        var computedHash = sBuilder.ToString();

        if (!string.Equals(computedHash, receivedHash, StringComparison.InvariantCultureIgnoreCase))
        {
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = orderId,
                Note = $"Computed hash '{computedHash}' is not equal to received hash '{receivedHash}'",
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            throw new NopException("Hash validation failed");
        }
    }

    /// <summary>
    /// Prepare billing address details
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
            $"&prod={WebUtility.UrlEncode($"Order_#{order.CustomOrderNumber}")}",
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
        /// More details: <c>https://verifone.cloud/docs/2checkout/Documentation/07Commerce/2Checkout-ConvertPlus/ConvertPlus_Buy-Links_Signature</c>
        var paramsToCache = parameters
            .Where(p => !p.Key.Equals("signature"))
            .OrderBy(p => p.Key)
            .Aggregate(string.Empty, (c, next) => $"{c}{next.Value.ToString().Length}{next.Value}");

        var sBuilder = new StringBuilder();

        var data = new HMACSHA256(Encoding.UTF8.GetBytes(_twoCheckoutPaymentSettings.SecretWord)).ComputeHash(Encoding.UTF8.GetBytes(paramsToCache));

        foreach (var theByte in data)
            sBuilder.AppendFormat("{0:x2}", theByte);

        return sBuilder.ToString();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handle IPN/INS request
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleIpnRequestAsync()
    {

        var request = _httpContextAccessor.HttpContext.Request;
        var form = request.HasFormContentType ? await request.ReadFormAsync() : new FormCollection(null);
        var parameters = request.HasFormContentType && form.Keys.Any()
            ? form.ToDictionary(param => param.Key.ToLower(), param => param.Value)
            : request.Query.ToDictionary(param => param.Key.ToLower(), param => param.Value);

        //define local function to get a value from the request Form or from the Query parameters
        string getValue(string key) => parameters.TryGetValue(key.ToLower(), out var value) ? value.ToString() : string.Empty;

        var messageType = getValue("message_type");
        var fraudStatus = getValue("fraud_status");

        if (!string.Equals(messageType, "FRAUD_STATUS_CHANGED", StringComparison.InvariantCultureIgnoreCase) || fraudStatus != "pass")
            return;

        //try to get an order by the number or by the identifier (used in previous plugin versions)
        var customOrderNumber = getValue("vendor_order_id");
        var order = await _orderService.GetOrderByCustomOrderNumberAsync(customOrderNumber)
            ?? await _orderService.GetOrderByIdAsync(int.TryParse(customOrderNumber, out var orderId) ? orderId : 0)
            ?? throw new NopException($"Order '{customOrderNumber}' not found");

        //save request info as order note for debug purposes
        var note = parameters.Aggregate("ConvertPlus INS:",
            (text, param) => $"{text}{Environment.NewLine}{param.Key}: {param.Value}");
        await _orderService.InsertOrderNoteAsync(new OrderNote
        {
            OrderId = order.Id,
            Note = note,
            DisplayToCustomer = false,
            CreatedOnUtc = DateTime.UtcNow
        });

        await VerifyPassedDataAsync(order.Id, getValue);

        var newPaymentStatus = PaymentStatus.Pending;

        var invoiceStatus = getValue("invoice_status");
        var paymentType = getValue("payment_type");

        if (invoiceStatus == "approved" || invoiceStatus == "deposited" || paymentType == "paypal ec")
            newPaymentStatus = PaymentStatus.Paid;

        //mark order as paid
        if (newPaymentStatus == PaymentStatus.Paid && _orderProcessingService.CanMarkOrderAsPaid(order))
            await _orderProcessingService.MarkOrderAsPaidAsync(order);
    }

    /// <summary>
    /// Handle return request
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the order id
    /// </returns>
    public async Task<int?> HandleReturnRequestAsync()
    {
        //More details: https://verifone.cloud/docs/2checkout/Documentation/07Commerce/2Checkout-ConvertPlus/Signature_validation_for_return_URL_via_ConvertPlus

        var request = _httpContextAccessor.HttpContext.Request;
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

        var receivedSignature = getValue("signature");
        var computedSignature = GetSignature(parameters);

        //verify the passed data by comparing signature
        if (!string.Equals(computedSignature, receivedSignature, StringComparison.InvariantCultureIgnoreCase))
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
        var builder = new StringBuilder($"{TwoCheckoutDefaults.ServiceUrl}?dynamic=1");

        var currency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

        builder.AppendFormat("&merchant={0}", _twoCheckoutPaymentSettings.AccountNumber);
        builder.AppendFormat("&currency={0}", currency?.CurrencyCode);
        builder.AppendFormat("&order-ext-ref={0}", order.CustomOrderNumber);

        var url = _nopUrlHelper.RouteUrl(TwoCheckoutDefaults.CompletedRouteName, null, _webHelper.GetCurrentRequestProtocol());
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