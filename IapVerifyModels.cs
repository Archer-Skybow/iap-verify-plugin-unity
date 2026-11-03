using System;

namespace Skybow.IapVerify
{
    // Tên field trùng tên trường JSON vì JsonUtility map theo tên field, nên viết camelCase thay vì PascalCase.
    // Trường vắng mặt trong JSON giữ giá trị mặc định (false, 0, null).
    [Serializable]
    public class IapVerifyResponse
    {
        public bool valid;
        public string reason;
        public string platform;
        public string environment;
        public bool isTester;
        public string testerType;
        public string state;
        public string productId;
        public string transactionId;
        // Chỉ Google: định danh duy nhất của purchase, luôn có (orderId có thể vắng với promo code)
        public string purchaseToken;
        public string originalTransactionId;
        public string purchaseTime;
        public string expiresTime;
        public int quantity;
        public bool acknowledged;
        public bool consumed;
        public string purchaseKind;
    }

    // Body lỗi: 4xx chỉ có error, 5xx có thêm code (INTERNAL_ERROR, STORE_UNAVAILABLE, PLATFORM_DISABLED)
    [Serializable]
    public class IapVerifyErrorBody
    {
        public string code;
        public string error;
    }
}
