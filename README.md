# IapVerify: client Unity cho iap-verify-api

Thư viện nhỏ để game Unity gửi receipt lên `POST /v1/verify/unity` và biết phải làm gì với purchase. Đặc tả API: [docs/API.md](../../../docs/API.md).

- Không phụ thuộc Unity IAP: chỉ nhận chuỗi receipt, nên dùng được với Unity IAP 4 lẫn 5.
- Không cần `MonoBehaviour`: callback chạy trên main thread. Có thêm bản `async` (`VerifyAsync`).
- Không cần Newtonsoft: request tự dựng JSON (bỏ trường rỗng, vì server trả 400 với chuỗi rỗng), response đọc bằng `JsonUtility`.
- Yêu cầu Unity 2020.2 trở lên (`UnityWebRequest.result`).

## Cài đặt

1. Copy cả thư mục `IapVerify/` vào `Assets/` của game, ví dụ `Assets/Plugins/IapVerify/`. Thư mục có sẵn `Skybow.IapVerify.asmdef` (namespace `Skybow.IapVerify`).
    - Code game nằm trong `Assembly-CSharp` thì dùng được ngay.
    - Code game nằm trong asmdef riêng thì thêm `Skybow.IapVerify` vào mục References của asmdef đó.
2. **Create → Skybow → IAP Verify Settings**: tạo asset cấu hình, điền:
    - `Base Url`: `https://139-162-45-38.ip.linodeusercontent.com` (đổi khi có domain chính thức);
    - `Api Key`: team backend gửi qua kênh riêng. Key nằm trong bản build nên coi như công khai; **không** commit vào repo công khai;
    - `Timeout Seconds`: mặc định 30.

## Kết quả và việc phải làm

`Verify` trả về `IapVerifyResult`; rẽ nhánh theo `Outcome`:

| `Outcome` | Khi nào | Cấp vật phẩm | Confirm purchase |
|---|---|---|---|
| `Valid` | HTTP 200, `valid: true` | **Có**, theo `Response.productId` | Có |
| `Invalid` | HTTP 200, `valid: false` (giả, sai app, đã hoàn tiền...) | Không | Có, để xoá khỏi hàng đợi store |
| `PaymentPending` | HTTP 200, `state: "pending"` (user chưa trả tiền xong) | Không | Không, chờ store báo lại |
| `RetryLater` | Mất mạng, timeout, 429, 5xx | Không | Không, thử lại sau `RetryAfterSeconds` (nếu có) |
| `ClientError` | 400, 401: request sai, sai API key, receipt Fake Store trong Editor | Không | Không; ghi log để sửa client |

- `ShouldGrant` và `ShouldConfirm` tóm gọn bảng trên.
- `IsTester` cho biết purchase có mất tiền thật không (License Tester, TestFlight/Sandbox).
- `PurchaseKey` là khoá chống cấp trùng: `purchaseToken` với Google (luôn có), `transactionId` với Apple. Đừng dùng `transactionId` cho Google: purchase bằng promo code không có `orderId`.
- `ToString()` in đủ thông tin để ghi log.

**Không** coi `RetryLater` hay `ClientError` là gian lận: các trường hợp đó server chưa kiểm tra được purchase. Google tự hoàn tiền purchase không được confirm trong 3 ngày, nên đừng giữ pending quá lâu chỉ vì lỗi mạng: store gửi lại purchase ở lần mở app sau, khi đó verify lại.

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

    [SerializeField] private IapVerifySettingsSO verifySettings;

    private StoreController store;
    private IapVerifyClient verifyClient;

    // TODO: lưu vào save game. OnPurchasePending có thể tới nhiều lần cho cùng một purchase (app tắt trước khi confirm)
    private readonly HashSet<string> grantedPurchaseKeys = new HashSet<string>();


    #region UNITY LIFECYCLE


    private async void Awake()
    {
        verifyClient = new IapVerifyClient(verifySettings);

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

        verifyClient.Verify(order.Info.Receipt, productId, result =>
        {
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

    verifyClient.Verify(product.receipt, product.definition.storeSpecificId, result =>
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
var result = await verifyClient.VerifyAsync(order.Info.Receipt, productId);

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
| Tắt mạng | `RetryLater` (HTTP 0) |
