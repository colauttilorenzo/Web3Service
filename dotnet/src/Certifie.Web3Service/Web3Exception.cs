using System;
using System.Text.Json.Serialization;

namespace Certifie.Web3
{
    /// <summary>Serializable error; deliberately excludes the RPC cause, which may include credentials or a signed transaction.</summary>
    public sealed class Web3ErrorInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "Web3Error";
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        [JsonPropertyName("transactionHash")] public string? TransactionHash { get; set; }
    }

    /// <summary>Error codes match the JavaScript package (e.g. <c>INVALID_ARGUMENT</c>, <c>TRANSACTION_REVERTED</c>).</summary>
    public sealed class Web3Exception : Exception
    {
        public Web3Exception(string code, string message, Exception? innerException = null, string? transactionHash = null)
            : base(message, innerException)
        {
            Code = code;
            TransactionHash = transactionHash;
        }

        public string Code { get; }
        public string? TransactionHash { get; }

        public Web3ErrorInfo ToInfo() => new Web3ErrorInfo { Code = Code, Message = Message, TransactionHash = TransactionHash };
    }
}
