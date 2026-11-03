namespace Skybow.IapVerify
{
    // Việc app phải làm với purchase sau khi verify (docs/API.md, mục "Khi nào gọi API")
    public enum IapVerifyOutcome
    {
        // Purchase hợp lệ: cấp vật phẩm theo Response.productId rồi confirm
        Valid,

        // Purchase không hợp lệ (giả, sai app, đã hoàn tiền...): không cấp, confirm để xoá khỏi hàng đợi store
        Invalid,

        // User chưa thanh toán xong (vd trả tiền mặt): không cấp, không confirm, chờ store báo lại
        PaymentPending,

        // Chưa kiểm tra được (mất mạng, 429, 5xx): không cấp, không confirm, thử lại sau
        RetryLater,

        // Request sai (400, 401): lỗi tích hợp, thử lại vẫn lỗi. Không cấp, không confirm, ghi log để sửa client
        ClientError,
    }
}
