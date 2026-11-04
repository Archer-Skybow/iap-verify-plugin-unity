using UnityEngine;

namespace Skybow.IapVerify
{
    // Tạo asset: Create → Skybow → IAP Verify Settings, đặt tại Resources/IapVerifySettings để IapVerifyClient tự đọc.
    // Cần nhiều môi trường (dev/production) thì tạo thêm asset và chọn bằng IapVerifyClient.Configure.
    // API key nằm trong bản build nên coi như công khai: server chống lạm dụng bằng giới hạn theo IP, không dựa vào key.
    [CreateAssetMenu(fileName = "IapVerifySettings", menuName = "Skybow/IAP Verify Settings")]
    public class IapVerifySettingsSO : ScriptableObject
    {
        public string BaseUrl => baseUrl;
        public string ApiKey => apiKey;
        public int TimeoutSeconds => timeoutSeconds;

        [SerializeField] private string baseUrl = "https://iap.archergame.mobi";
        [SerializeField] private string apiKey;
        [SerializeField] private int timeoutSeconds = 30;
    }
}
