using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Skybow.IapVerify
{
    // Gọi POST /v1/verify/unity của iap-verify-api (docs/API.md).
    // Không phụ thuộc Unity IAP: chỉ nhận chuỗi receipt, nên dùng được với Unity IAP 4 lẫn 5.
    // Callback chạy trên main thread của Unity, không cần MonoBehaviour.
    // Cấu hình tự đọc từ Resources/IapVerifySettings ở lần gọi đầu; gọi Configure để ghi đè (vd đổi môi trường).
    public static class IapVerifyClient
    {
        private const string VerifyPath = "/v1/verify/unity";
        private const string SettingsResourcePath = "IapVerifySettings";
        private const int DefaultTimeoutSeconds = 30;
        private const int DefaultRetryAfterSeconds = 60;

        private static string baseUrl;
        private static string apiKey;
        private static int timeoutSeconds = DefaultTimeoutSeconds;
        private static bool isConfigured;


        #region PUBLIC API


        public static void Configure(IapVerifySettingsSO settings)
        {
            if (settings == null)
                return;

            Configure(settings.BaseUrl, settings.ApiKey, settings.TimeoutSeconds);
        }

        public static void Configure(string baseUrl, string apiKey, int timeoutSeconds = DefaultTimeoutSeconds)
        {
            IapVerifyClient.baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            IapVerifyClient.apiKey = apiKey;
            IapVerifyClient.timeoutSeconds = timeoutSeconds;
            isConfigured = true;
        }

        // receipt: nguyên chuỗi receipt của Unity IAP (v4: product.receipt, v5: order.Info.Receipt), không sửa.
        // productId: product.definition.storeSpecificId. Khác sản phẩm thật trong receipt thì nhận Invalid.
        // isSubscription: gói subscription của Google phải bật cờ này.
        public static void Verify(string receipt, string productId, Action<IapVerifyResult> onDone, bool isSubscription = false)
        {
            if (!EnsureConfigured())
            {
                onDone?.Invoke(new IapVerifyResult(IapVerifyOutcome.ClientError, 0, null, null, $"Chưa cấu hình: tạo asset {SettingsResourcePath} trong thư mục Resources, hoặc gọi IapVerifyClient.Configure", 0));
                return;
            }

            if (string.IsNullOrEmpty(receipt))
            {
                onDone?.Invoke(new IapVerifyResult(IapVerifyOutcome.ClientError, 0, null, null, "receipt rỗng", 0));
                return;
            }

            var body = BuildBody(receipt, productId, isSubscription);
            var request = new UnityWebRequest(baseUrl + VerifyPath, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
            };
            request.SetRequestHeader("Content-Type", "application/json");

            // Thiếu key thì không gửi header (SetRequestHeader ném lỗi với giá trị rỗng); server trả 401 → ClientError
            if (!string.IsNullOrEmpty(apiKey))
                request.SetRequestHeader("x-api-key", apiKey);

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                var result = ToResult(request);
                request.Dispose();
                onDone?.Invoke(result);
            };
        }

        public static Task<IapVerifyResult> VerifyAsync(string receipt, string productId, bool isSubscription = false)
        {
            var completion = new TaskCompletionSource<IapVerifyResult>();
            Verify(receipt, productId, completion.SetResult, isSubscription);
            return completion.Task;
        }


        #endregion


        #region PRIVATE METHODS


        // Editor tắt Domain Reload (Enter Play Mode Options) thì biến static còn giữ từ lần Play trước: xoá khi bắt đầu Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            baseUrl = null;
            apiKey = null;
            timeoutSeconds = DefaultTimeoutSeconds;
            isConfigured = false;
        }

        private static bool EnsureConfigured()
        {
            if (isConfigured)
                return true;

            var settings = Resources.Load<IapVerifySettingsSO>(SettingsResourcePath);

            if (settings == null)
            {
                Debug.LogError($"[IapVerify] Không tìm thấy Resources/{SettingsResourcePath}: tạo asset bằng Create → Skybow → IAP Verify Settings, đặt trong thư mục Resources");
                return false;
            }

            Configure(settings);
            return true;
        }

        // Tự dựng JSON thay vì JsonUtility.ToJson: JsonUtility ghi chuỗi null thành "" mà server trả 400 với chuỗi rỗng.
        // Trường không dùng phải vắng mặt hẳn khỏi body.
        private static string BuildBody(string receipt, string productId, bool isSubscription)
        {
            var json = new StringBuilder(receipt.Length + 256);
            json.Append('{');
            AppendField(json, "receipt", receipt);
            AppendField(json, "productId", productId);
            // Giúp server tra thẳng đúng app Apple; receipt Google bỏ qua trường này
            AppendField(json, "bundleId", Application.identifier);

            if (isSubscription)
                AppendField(json, "type", "subscription");

            json.Append('}');
            return json.ToString();
        }

        private static void AppendField(StringBuilder json, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (json.Length > 1)
                json.Append(',');

            json.Append('"').Append(name).Append("\":");
            AppendEscaped(json, value);
        }

        private static void AppendEscaped(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        json.Append("\\\"");
                        break;

                    case '\\':
                        json.Append("\\\\");
                        break;

                    case '\n':
                        json.Append("\\n");
                        break;

                    case '\r':
                        json.Append("\\r");
                        break;

                    case '\t':
                        json.Append("\\t");
                        break;

                    default:
                        if (c < ' ')
                            json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            json.Append(c);

                        break;
                }
            }
            json.Append('"');
        }

        private static IapVerifyResult ToResult(UnityWebRequest request)
        {
            var code = request.responseCode;
            var text = request.downloadHandler?.text;

            // Không có response HTTP: mất mạng, timeout, lỗi DNS/TLS
            if (request.result == UnityWebRequest.Result.ConnectionError || code == 0)
                return new IapVerifyResult(IapVerifyOutcome.RetryLater, 0, null, null, request.error, 0);

            if (code == 200)
                return FromVerifyResponse(text);

            var error = ParseJson<IapVerifyErrorBody>(text);
            var errorCode = error?.code;
            var message = error?.error ?? request.error;

            if (code == 429)
            {
                var retryAfter = ParseRetryAfter(request.GetResponseHeader("retry-after"));
                return new IapVerifyResult(IapVerifyOutcome.RetryLater, code, null, errorCode, message, retryAfter);
            }

            // 5xx: store hoặc server tạm lỗi, chưa kiểm tra được purchase
            if (code >= 500)
                return new IapVerifyResult(IapVerifyOutcome.RetryLater, code, null, errorCode, message, 0);

            // 400, 401, 403...: request sai, sai key, hoặc key chưa gắn với app này (APP_NOT_ALLOWED); thử lại vẫn nhận cùng lỗi
            return new IapVerifyResult(IapVerifyOutcome.ClientError, code, null, errorCode, message, 0);
        }

        private static IapVerifyResult FromVerifyResponse(string text)
        {
            var response = ParseJson<IapVerifyResponse>(text);

            // HTTP 200 mà không phải JSON của API (vd trang của wifi công cộng chặn giữa đường): coi như chưa kiểm tra được
            if (response == null || string.IsNullOrEmpty(response.platform))
                return new IapVerifyResult(IapVerifyOutcome.RetryLater, 200, null, null, "Response không đúng định dạng", 0);

            if (response.valid)
                return new IapVerifyResult(IapVerifyOutcome.Valid, 200, response, null, null, 0);

            if (response.state == "pending")
                return new IapVerifyResult(IapVerifyOutcome.PaymentPending, 200, response, null, response.reason, 0);

            return new IapVerifyResult(IapVerifyOutcome.Invalid, 200, response, null, response.reason, 0);
        }

        private static T ParseJson<T>(string text) where T : class
        {
            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                return JsonUtility.FromJson<T>(text);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static int ParseRetryAfter(string header)
        {
            if (int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                return seconds;

            return DefaultRetryAfterSeconds;
        }


        #endregion
    }
}
