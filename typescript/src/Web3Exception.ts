/** Serialized form of a Web3Error: the same JSON as Web3ErrorInfo in .NET. */
export interface Web3ErrorJSON {
  name: string;
  code: string;
  message: string;
  transactionHash?: string;
}

/** Every error raised by the package. It is the equivalent of Web3Exception in .NET; the name Web3Error is kept for compatibility. */
export class Web3Error extends Error {
  readonly code: string;
  readonly transactionHash?: string;

  constructor(code: string, message: string, options?: { cause?: unknown; transactionHash?: string }) {
    super(message, { cause: options?.cause });
    this.name = 'Web3Error';
    this.code = code;
    this.transactionHash = options?.transactionHash;
  }

  /** Deliberately excludes the RPC cause, which may include credentials or a signed transaction. */
  toJSON(): Web3ErrorJSON {
    return {
      name: this.name,
      code: this.code,
      message: this.message,
      ...(this.transactionHash ? { transactionHash: this.transactionHash } : {}),
    };
  }
}

/** The blockchain of the provider does not offer the operation, e.g. deploy on Bitcoin. The equivalent of NotImplementedException in .NET. */
export class NotImplementedError extends Web3Error {
  constructor(message: string) {
    super('NOT_IMPLEMENTED', message);
    this.name = 'NotImplementedError';
  }
}
