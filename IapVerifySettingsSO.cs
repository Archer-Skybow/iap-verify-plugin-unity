using UnityEngine;

namespace Skybow.IapVerify
{
    // Tạo asset: Create → Skybow → IAP Verify Settings. Mỗi môi trường (dev/production) một asset.
    // API key nằm trong bản build nên coi như công khai: server chống lạm dụng bằng giới hạn theo IP, không dựa vào key.
    [CreateAssetMenu(fileName = "IapVerifySettings", menuName = "Skybow/IAP Verify Settings")]
    public class IapVerifySettingsSO : ScriptableObject
    {
        public string BaseUrl => baseUrl;
        public string ApiKey => apiKey;
        public int TimeoutSeconds => timeoutSeconds;

        [SerializeField] private string baseUrl = "https://139-162-45-38.ip.linodeusercontent.com";
        [SerializeField] private string apiKey;
        [SerializeField] private int timeoutSeconds = 30;
    }
}
