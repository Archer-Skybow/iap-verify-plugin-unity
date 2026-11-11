# IapVerify: client Unity cho iap-verify-api

Thư viện nhỏ để game Unity gửi receipt lên `POST /v1/verify/unity` và biết phải làm gì với purchase. Đặc tả API: [docs/API.md](../../../docs/API.md).

> **Lưu ý:** `Android Api Key` / `Ios Api Key` trong `Resources/IapVerifySettings.asset` đi kèm thư viện chỉ là **key ví dụ**, không dùng cho game thật. Liên hệ team backend để lấy API key của game rồi điền vào asset cấu hình của game (xem mục [Cài đặt](#cài-đặt)). Mỗi game có key riêng, thường dùng chung cho bản Android và iOS (khi đó điền cùng một key vào cả hai ô), và key chỉ verify được app của game đó: dùng key của game khác, hoặc backend chưa gắn key vào app (ví dụ mới gắn cho Android, quên iOS), thì nhận `ClientError` (HTTP 403). Để trống key của nền tảng đang build thì nhận `ClientError` (HTTP 401) và Console có log lỗi `[IapVerify] ... chưa điền ...`.

- Không phụ thuộc Unity IAP: chỉ nhận chuỗi receipt, nên dùng được với Unity IAP 4 lẫn 5.
- Gọi thẳng `IapVerifyClient.Verify(...)` (static class), không cần khởi tạo hay `MonoBehaviour`: callback chạy trên main thread. Có thêm bản `async` (`VerifyAsync`).
- Không cần Newtonsoft: request tự dựng JSON (bỏ trường rỗng, vì server trả 400 với chuỗi rỗng), response đọc bằng `JsonUtility`.
- Yêu cầu Unity 2020.2 trở lên (`UnityWebRequest.result`).

## Cài đặt

1. Copy cả thư mục `IapVerify/` vào `Assets/` của game, ví dụ `Assets/Plugins/IapVerify/`. Thư mục có sẵn `Skybow.IapVerify.asmdef` (namespace `Skybow.IapVerify`).
    - Code game nằm trong `Assembly-CSharp` thì dùng được ngay.
    - Code game nằm trong asmdef riêng thì thêm `Skybow.IapVerify` vào mục References của asmdef đó.
2. **Create → Skybow → IAP Verify Settings**: tạo asset cấu hình **trong một thư mục `Resources`**, giữ đúng tên **`IapVerifySettings`** (ví dụ `Assets/Resources/IapVerifySettings.asset`). Client tự đọc asset này ở lần gọi đầu. Điền:
    - `Base Url`: `https://iap.archergame.mobi` (mặc định của asset);
    - `Android Api Key`, `Ios Api Key`: key của game cho từng nền tảng, team backend gửi qua kênh riêng. Thường hai ô cùng một key; backend gắn key nào cho app nào thì điền đúng key đó. Client chọn key lúc build theo nền tảng (`UNITY_IOS` → `Ios Api Key`, còn lại → `Android Api Key`; trong Editor theo Build Target đang chọn). Asset tạo từ bản cũ chỉ có `Api Key`: giá trị đó tự chuyển sang `Android Api Key`, cần điền thêm `Ios Api Key`. Key nằm trong bản build nên coi như công khai; **không** commit vào repo công khai;
    - `Timeout Seconds`: mặc định 30.
3. Tuỳ chọn: gọi `IapVerifyClient.Configure(...)` trước lần verify đầu để ghi đè cấu hình, ví dụ chọn asset dev/production theo build, hoặc truyền thẳng `baseUrl` / `apiKey`:
    ```csharp
    IapVerifyClient.Configure(Debug.isDebugBuild ? devSettings : productionSettings);
    ```
    Không có asset trong `Resources` mà cũng không gọi `Configure` thì `Verify` trả `ClientError` và log lỗi.

## Kết quả và việc phải làm

`Verify` trả về `IapVerifyResult`; rẽ nhánh theo `Outcome`:

| `Outcome` | Khi nào | Cấp vật phẩm | Confirm purchase |
|---|---|---|---|
| `Valid` | HTTP 200, `valid: true` | **Có**, theo `Response.productId` | Có |
| `Invalid` | HTTP 200, `valid: false` (giả, sai app, đã hoàn tiền...) | Không | Có, để xoá khỏi hàng đợi store |
| `PaymentPending` | HTTP 200, `state: "pending"` (user chưa trả tiền xong) | Không | Không, chờ store báo lại |
| `RetryLater` | Mất mạng, timeout, 429, 5xx | Không | Không, thử lại sau `RetryAfterSeconds` (nếu có) |
| `ClientError` | 400, 401, 403: request sai, sai API key, key chưa gắn với app này, receipt Fake Store trong Editor | Không | Không; ghi log để sửa client |

- `ShouldGrant` và `ShouldConfirm` tóm gọn bảng trên.
- `IsTester` cho biết purchase có mất tiền thật không (License Tester, TestFlight/Sandbox).
- `PurchaseKey` là khoá chống cấp trùng: `purchaseToken` với Google (luôn có), `transactionId` với Apple. Đừng dùng `transactionId` cho Google: purchase bằng promo code không có `orderId`.
- `ToString()` in đủ thông tin để ghi log.

**Không** coi `RetryLater` hay `ClientError` là gian lận: các trường hợp đó server chưa kiểm tra được purchase. Google tự hoàn tiền purchase không được confirm trong 3 ngày, nên đừng giữ pending quá lâu chỉ vì lỗi mạng: store gửi lại purchase ở lần mở app sau, khi đó verify lại.

## JSON response theo từng trường hợp

Các ví dụ dưới đây là body server trả về; client tự đọc vào `IapVerifyResult`, game không cần parse.

- HTTP 200: body nằm trong `result.Response` (kiểu `IapVerifyResponse`).
- Mã khác 200: `Response = null`. Trường `code` của body nằm trong `result.ErrorCode`, trường `error` nằm trong `result.Message`.

Trường không có giá trị thì vắng mặt trong JSON, nên trong `Response` là chuỗi rỗng hoặc `0`. Nội dung `reason` và `error` có thể thay đổi: chỉ dùng để ghi log, không dùng để rẽ nhánh code.

### `Valid`

User thật mua trên Google Play. `PurchaseKey` = `purchaseToken`:

```json
{
  "valid": true,
  "platform": "google",
  "environment": "production",
  "isTester": false,
  "testerType": null,
  "state": "purchased",
  "productId": "gem_100",
  "transactionId": "GPA.3312-4567-8901-23456",
  "purchaseToken": "kmgfhbnchlmkcclecmojnblf.AO-J1Oz...",
  "purchaseTime": "2026-11-02T07:00:00.000Z",
  "quantity": 1,
  "acknowledged": false,
  "consumed": false,
  "purchaseKind": "paid"
}
```

License Tester trên Google Play: giống trên, chỉ khác các trường sau (`IsTester = true`):

```json
{
  "environment": "sandbox",
  "isTester": true,
  "testerType": "google_license_tester"
}
```

Tester mua trên bản TestFlight hoặc bằng tài khoản Sandbox. `PurchaseKey` = `transactionId`; Apple không có `purchaseToken`, `acknowledged`, `consumed`, `purchaseKind`:

```json
{
  "valid": true,
  "platform": "apple",
  "environment": "sandbox",
  "isTester": true,
  "testerType": "apple_sandbox",
  "state": "purchased",
  "productId": "gem_100",
  "transactionId": "2000000123456789",
  "originalTransactionId": "2000000123456789",
  "purchaseTime": "2026-11-02T07:00:00.000Z",
  "quantity": 1
}
```

### `Invalid`

Receipt giả hoặc không tồn tại: server không tra được purchase, nên `environment` là `unknown` và không có `productId`:

```json
{
  "valid": false,
  "reason": "Không tìm thấy giao dịch trên App Store (Production lẫn Sandbox)",
  "platform": "apple",
  "environment": "unknown",
  "isTester": false,
  "testerType": null,
  "state": "unknown"
}
```

Purchase có thật nhưng `productId` gửi lên khác sản phẩm trong receipt (ví dụ mua gói rẻ, khai gói đắt). `productId` trong response là sản phẩm **thật**:

```json
{
  "valid": false,
  "reason": "productId gem_10 không khớp gem_100",
  "platform": "google",
  "environment": "production",
  "isTester": false,
  "testerType": null,
  "state": "purchased",
  "productId": "gem_10",
  "transactionId": "GPA.3312-4567-8901-23456",
  "purchaseToken": "kmgfhbnchlmkcclecmojnblf.AO-J1Oz...",
  "purchaseTime": "2026-11-02T07:00:00.000Z",
  "quantity": 1,
  "acknowledged": false,
  "consumed": false,
  "purchaseKind": "paid"
}
```

Các lý do `Invalid` khác cùng dạng trên, chỉ khác `reason` và `state`: đã hoàn tiền (`refunded`), đã huỷ (`canceled`), subscription hết hạn (`expired`), sai chữ ký receipt, package/bundleId không thuộc danh sách app của server.

### `PaymentPending`

User chọn hình thức trả sau (ví dụ trả tiền mặt ở cửa hàng) và chưa trả xong. Client nhận ra trường hợp này qua `state: "pending"`:

```json
{
  "valid": false,
  "reason": "purchaseState = pending",
  "platform": "google",
  "environment": "production",
  "isTester": false,
  "testerType": null,
  "state": "pending",
  "productId": "gem_100",
  "purchaseToken": "kmgfhbnchlmkcclecmojnblf.AO-J1Oz...",
  "purchaseTime": "2026-11-02T07:00:00.000Z",
  "quantity": 1,
  "acknowledged": false,
  "consumed": false,
  "purchaseKind": "paid"
}
```

### `RetryLater`

| HTTP | Body | Ý nghĩa |
|---|---|---|
| 0 | không có | Mất mạng, timeout, lỗi DNS/TLS. `Message` là `UnityWebRequest.error` |
| 429 | `{"error": "Rate limit exceeded, retry in 1 minute"}` | Vượt 120 request/phút/IP |
| 429 | `{"error": "Quá nhiều request không hợp lệ, thử lại sau"}` | IP bị chặn tạm vì gửi nhiều receipt không tra được hoặc request sai |
| 500 | `{"code": "INTERNAL_ERROR", "error": "Lỗi hệ thống, thử lại sau"}` | Lỗi server |
| 502, 503 | `{"code": "STORE_UNAVAILABLE", "error": "Không xác thực được với store, thử lại sau"}` | Google/Apple tạm lỗi, hoặc store từ chối credential của server |
| 503 | `{"code": "PLATFORM_DISABLED", "error": "Platform không được hỗ trợ"}` | Server chưa bật store này hoặc chưa khai báo app nào: báo team backend |

Với 429, header `retry-after` (giây) được đọc vào `RetryAfterSeconds`; thiếu header thì mặc định 60. Với 5xx, `RetryAfterSeconds = 0`.

HTTP 200 mà body không phải JSON của API (ví dụ trang đăng nhập của wifi công cộng chặn giữa đường) cũng là `RetryLater`, với `Message = "Response không đúng định dạng"`.

### `ClientError`

| HTTP | Body | Ý nghĩa |
|---|---|---|
| 400 | `{"error": "Receipt từ Unity Fake Store (Editor) không thể xác thực"}` | Chạy trong Editor |
| 400 | `{"error": "Store không được hỗ trợ: <Store>"}` | Receipt không phải `GooglePlay` / `AppleAppStore` |
| 400 | `{"error": "receipt thiếu Payload"}`, `{"error": "... không phải JSON hợp lệ"}` | Chuỗi receipt bị sửa hoặc cắt |
| 400 | `{"error": "body/productId must NOT have more than 256 characters"}` | Body sai schema: chuỗi rỗng, quá dài, thiếu `receipt` |
| 401 | `{"error": "Unauthorized"}` | Thiếu hoặc sai API key, hoặc key đã bị thu hồi |
| 403 | `{"code": "APP_NOT_ALLOWED", "error": "API key ... không có trong apiKeys của app ..."}` | Key chưa được gắn với app này (package/bundle ID): dùng nhầm key của game khác, hoặc backend chưa gắn key cho bản Android/iOS này. Báo team backend. `ErrorCode = "APP_NOT_ALLOWED"` |
| 0 | không gửi request | Thiếu cấu hình (`Message` bắt đầu bằng `Chưa cấu hình`) hoặc receipt rỗng (`Message = "receipt rỗng"`) |

## Ví dụ Unity IAP 5

Gắn vào `MonoBehaviour` đang quản lý IAP. Phần khởi tạo `StoreController` giữ nguyên như game đang có; chỉ cần `OnPurchasePending` gọi `VerifyAndFinish`. Phải đăng ký event **trước** `Connect()`, vì purchase pending từ phiên trước có thể tới ngay.

```csharp
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Skybow.IapVerify;
using UnityEngine;
using UnityEngine.Purchasing;

public class IapPurchaseFlow : MonoBehaviour
{
    // Thử lại trong phiên: chờ 5s, 15s, 60s; sau đó để store gửi lại ở lần mở app sau
    private static readonly int[] RetryDelaySeconds = { 5, 15, 60 };

    private StoreController store;

    // TODO: lưu vào save game. OnPurchasePending có thể tới nhiều lần cho cùng một purchase (app tắt trước khi confirm)
    private readonly HashSet<string> grantedPurchaseKeys = new HashSet<string>();


    #region UNITY LIFECYCLE


    private async void Awake()
    {
        store = UnityIAPServices.StoreController();
        store.OnPurchasePending += OnPurchasePending;
        store.OnPurchaseConfirmed += OnPurchaseConfirmed;
        store.OnPurchaseFailed += OnPurchaseFailed;
        store.OnPurchaseDeferred += OnPurchaseDeferred;
        store.OnStoreDisconnected += OnStoreDisconnected;
        // ... OnProductsFetched / OnProductsFetchFailed, FetchProducts như game đang có

        await store.Connect();
    }


    #endregion


    #region PRIVATE METHODS


    private void VerifyAndFinish(PendingOrder order, int attempt)
    {
        var product = order.CartOrdered.Items().FirstOrDefault()?.Product;
        var productId = product?.definition.storeSpecificId;
        var isSubscription = product != null && product.definition.type == ProductType.Subscription;

        IapVerifyClient.Verify(order.Info.Receipt, productId, result =>
        {
            TrackVerifiedPurchase(result);

            switch (result.Outcome)
            {
                case IapVerifyOutcome.Valid:
                    GrantOnce(result);
                    store.ConfirmPurchase(order);

                    break;

                case IapVerifyOutcome.Invalid:
                    Debug.LogWarning($"[IAP] Purchase không hợp lệ, không cấp: {result}");
                    store.ConfirmPurchase(order);

                    break;

                case IapVerifyOutcome.PaymentPending:
                    Debug.Log($"[IAP] Chờ user thanh toán xong: {result}");

                    break;

                case IapVerifyOutcome.RetryLater:
                    Debug.LogWarning($"[IAP] Chưa verify được, thử lại sau: {result}");

                    if (attempt < RetryDelaySeconds.Length)
                        StartCoroutine(RetryVerify(order, attempt, result.RetryAfterSeconds));

                    break;

                case IapVerifyOutcome.ClientError:
                    Debug.LogError($"[IAP] Request verify sai, kiểm tra cấu hình client: {result}");

                    break;
            }
        }, isSubscription);
    }

    private static void TrackVerifiedPurchase(IapVerifyResult result)
    {
        // Response chỉ có khi HTTP 200 - ClientError trả Response = null
        if (result.Outcome != IapVerifyOutcome.Valid || result.IsTester)
        {
            Debug.Log("Purchase ignored");
            return;
        }

        var adjustEvent = new AdjustEvent("example_aj_iap_event");
        // adjustEvent.SetRevenue(price, currency);
        // adjustEvent.ProductId = productId;

#if UNITY_IOS
        adjustEvent.TransactionId = result.Response.transactionId;
#else
        adjustEvent.PurchaseToken = result.Response.purchaseToken;
#endif

        Adjust.TrackEvent(adjustEvent);
        Debug.Log("Purchase tracked");
    }

    private IEnumerator RetryVerify(PendingOrder order, int attempt, int retryAfterSeconds)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(RetryDelaySeconds[attempt], retryAfterSeconds));

        VerifyAndFinish(order, attempt + 1);
    }

    private void GrantOnce(IapVerifyResult result)
    {
        // PurchaseKey: purchaseToken (Google) / transactionId (Apple)
        if (!grantedPurchaseKeys.Add(result.PurchaseKey))
            return;

        // Cấp theo productId trong response (sản phẩm thật trong receipt), không theo sản phẩm app tưởng là đã mua
        var response = result.Response;

        // TODO: cấp vật phẩm của response.productId (x response.quantity); result.IsTester nếu cần xử lý riêng tester
    }

    private void OnPurchasePending(PendingOrder order)
    {
        VerifyAndFinish(order, 0);
    }

    private void OnPurchaseConfirmed(Order order)
    {
        if (order is FailedOrder failed)
            Debug.LogError($"[IAP] Confirm thất bại: {failed.FailureReason} - {failed.Details}");
    }

    private void OnPurchaseFailed(FailedOrder order)
    {
        Debug.LogWarning($"[IAP] Mua thất bại: {order.FailureReason} - {order.Details}");
    }

    private void OnPurchaseDeferred(DeferredOrder order)
    {
        // Ask-to-Buy / thanh toán trả sau: không cấp, chờ OnPurchasePending khi được duyệt
        Debug.Log("[IAP] Purchase đang chờ duyệt");
    }

    private void OnStoreDisconnected(StoreConnectionFailureDescription description)
    {
        Debug.LogWarning($"[IAP] Mất kết nối store: {description.Message}");
    }


    #endregion
}
```

## Ví dụ Unity IAP 4

```csharp
public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
{
    var product = args.purchasedProduct;
    var isSubscription = product.definition.type == ProductType.Subscription;

    IapVerifyClient.Verify(product.receipt, product.definition.storeSpecificId, result =>
    {
        if (result.ShouldGrant)
            GrantOnce(result);

        // Invalid cũng confirm để xoá khỏi hàng đợi; PaymentPending / RetryLater / ClientError giữ pending
        if (result.ShouldConfirm)
            storeController.ConfirmPendingPurchase(product);
        else
            Debug.LogWarning($"[IAP] Giữ purchase pending: {result}");
    }, isSubscription);

    // Luôn trả Pending: chỉ confirm sau khi server trả lời
    return PurchaseProcessingResult.Pending;
}
```

## Dùng với async/await

```csharp
var result = await IapVerifyClient.VerifyAsync(order.Info.Receipt, productId);

if (result.ShouldGrant)
    GrantOnce(result);
```

## Kiểm thử

| Cách test | Kết quả mong đợi |
|---|---|
| Chạy trong Editor (Fake Store) | `ClientError` (HTTP 400): receipt Fake Store không xác thực được. Đây là dấu hiệu đã gọi tới server và API key đúng |
| Android, Gmail trong Play Console → License testing | `Valid`, `IsTester = true`, `testerType = "google_license_tester"` |
| iOS bản TestFlight hoặc tài khoản Sandbox | `Valid`, `IsTester = true`, `testerType = "apple_sandbox"` |
| Sai API key | `ClientError` (HTTP 401) |
| API key chưa gắn với app (key của game khác) | `ClientError` (HTTP 403, `ErrorCode = "APP_NOT_ALLOWED"`) |
| Tắt mạng | `RetryLater` (HTTP 0) |
