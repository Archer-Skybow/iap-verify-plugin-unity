using UnityEngine;
using UnityEngine.Serialization;

namespace Skybow.IapVerify
{
    // Tạo asset: Create → Skybow → IAP Verify Settings, đặt tại Resources/IapVerifySettings để IapVerifyClient tự đọc.
    // Cần nhiều môi trường (dev/production) thì tạo thêm asset và chọn bằng IapVerifyClient.Configure.
    // API key nằm trong bản build nên coi như công khai: server chống lạm dụng bằng giới hạn theo IP, không dựa vào key.
    [CreateAssetMenu(fileName = "IapVerifySettings", menuName = "Skybow/IAP Verify Settings")]
    public class IapVerifySettingsSO : ScriptableObject
    {
        public string BaseUrl => baseUrl;
        public string AndroidApiKey => androidApiKey;
        public string IosApiKey => iosApiKey;
        public int TimeoutSeconds => timeoutSeconds;

        // Key của nền tảng đang build, chọn lúc compile: trong Editor theo Build Target đang chọn.
        // Không lấy key Android thay cho iOS khi thiếu: key chưa gắn với app iOS sẽ bị server trả 401/403, nên thiếu thì báo lỗi ngay.
#if UNITY_IOS
        public string ApiKey => iosApiKey;
#else
        public string ApiKey => androidApiKey;
#endif

        [SerializeField] private string baseUrl = "https://iap.archergame.mobi";

        // Asset cũ chỉ có một trường apiKey (đang điền key Android): FormerlySerializedAs giữ lại giá trị đó.
        [FormerlySerializedAs("apiKey")]
        [SerializeField] private string androidApiKey;
        [SerializeField] private string iosApiKey;

        [SerializeField] private int timeoutSeconds = 30;
    }
}
