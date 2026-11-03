namespace Skybow.IapVerify
{
    public class IapVerifyResult
    {
        public IapVerifyOutcome Outcome { get; }

        // 0 khi không nhận được response HTTP (mất mạng, timeout)
        public long HttpCode { get; }

        // Chỉ có khi HTTP 200
        public IapVerifyResponse Response { get; }

        // Mã lỗi của 5xx (vd STORE_UNAVAILABLE), dùng để rẽ nhánh thay cho Message
        public string ErrorCode { get; }

        // Mô tả để ghi log: reason khi Invalid, error của server hoặc lỗi mạng khi thất bại
        public string Message { get; }

        // Số giây nên chờ trước khi thử lại (từ header retry-after của 429), 0 = tự chọn
        public int RetryAfterSeconds { get; }

        public bool ShouldGrant => Outcome == IapVerifyOutcome.Valid;

        // Confirm khi đã có kết luận cuối cùng; các trường hợp khác giữ purchase pending để store gửi lại
        public bool ShouldConfirm => Outcome == IapVerifyOutcome.Valid || Outcome == IapVerifyOutcome.Invalid;

        public bool IsTester => Response != null && Response.isTester;

        // Khoá chống cấp trùng một purchase: Google purchaseToken (luôn có), Apple transactionId.
        // null khi không có Response.
        public string PurchaseKey => Response == null
            ? null
            : string.IsNullOrEmpty(Response.purchaseToken) ? Response.transactionId : Response.purchaseToken;

        internal IapVerifyResult(IapVerifyOutcome outcome, long httpCode, IapVerifyResponse response, string errorCode, string message, int retryAfterSeconds)
        {
            Outcome = outcome;
            HttpCode = httpCode;
            Response = response;
            ErrorCode = errorCode;
            Message = message;
            RetryAfterSeconds = retryAfterSeconds;
        }

        public override string ToString()
        {
            if (Response != null)
                return $"{Outcome} (HTTP {HttpCode}) productId={Response.productId} transactionId={Response.transactionId} isTester={Response.isTester} state={Response.state} reason={Response.reason}";

            return $"{Outcome} (HTTP {HttpCode}) code={ErrorCode} message={Message}";
        }
    }
}
