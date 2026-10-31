namespace Ranvier.Dom

open System

/// <summary>The view type a getRandomValues call fills, numbered as the native <c>RV_DOM_ARRAY_BUFFER_VIEW_*</c> kinds.</summary>
type RandomValuesKind =
    | Int8 = 0
    | Uint8 = 1
    | Uint8Clamped = 2
    | Int16 = 3
    | Uint16 = 4
    | Int32 = 5
    | Uint32 = 6
    | Float16 = 7
    | Float32 = 8
    | Float64 = 9
    | BigInt64 = 10
    | BigUint64 = 11
    | DataView = 12

/// <summary>A Web Crypto error with the engine's exception name, such as TypeMismatchError or QuotaExceededError.</summary>
exception CryptoException of name: string * detail: string with
    override this.Message = this.name + ": " + this.detail

/// <summary>A Web Crypto result, or the reason the provider has no implementation of the operation.</summary>
[<RequireQualifiedAccess>]
type CryptoOutcome<'T> =
    | Value of value: 'T
    | Unsupported of reason: string

/// <summary>A SHA digest algorithm of SubtleCrypto.digest.</summary>
[<RequireQualifiedAccess>]
type DigestAlgorithm =
    | Sha1
    | Sha256
    | Sha384
    | Sha512

/// <summary>A SubtleCrypto operation.</summary>
[<RequireQualifiedAccess>]
type SubtleOperation =
    | Digest
    | Encrypt
    | Decrypt
    | Sign
    | Verify
    | GenerateKey
    | DeriveKey
    | DeriveBits
    | ImportKey
    | ExportKey
    | WrapKey
    | UnwrapKey

/// <summary>Random values, random UUIDs and SHA digests of one document's crypto object.</summary>
/// <remarks>
/// Digest supports SHA-1, SHA-256, SHA-384 and SHA-512 on every provider. Every other SubtleCrypto algorithm and
/// operation reads as <c>CryptoOutcome.Unsupported</c>. A browser provider in an insecure context has no
/// randomUUID or SubtleCrypto, which also read as Unsupported.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type CryptoService =
    inherit IDisposable
    /// <summary>
    /// Fills <c>count</c> bytes of <c>bytes</c> from <c>offset</c>, as one view of <c>kind</c>, with cryptographically
    /// random values. The bytes are unchanged when the call raises.
    /// </summary>
    /// <remarks>
    /// Raises CryptoException TypeMismatchError for a floating-point kind or DataView, checked first;
    /// QuotaExceededError for more than 65536 bytes; RangeError when <c>count</c> is not a multiple of the element size.
    /// An empty range succeeds.
    /// </remarks>
    abstract GetRandomValues: kind: RandomValuesKind * bytes: byte array * offset: int * count: int -> unit
    /// <summary>A version 4 UUID in lowercase hexadecimal, 36 characters long.</summary>
    abstract RandomUUID: unit -> CryptoOutcome<string>
    /// <summary>The digest of <c>count</c> bytes of <c>data</c> from <c>offset</c>.</summary>
    /// <remarks>
    /// The algorithm name matches case-insensitively, as WebCrypto normalizes it. A name outside the SHA family is
    /// Unsupported. The provider copies the bytes before the call returns.
    /// </remarks>
    abstract Digest: algorithm: string * data: byte array * offset: int * count: int -> Async<CryptoOutcome<byte array>>
    /// <summary>Whether the provider implements <c>operation</c> for the algorithm named <c>algorithm</c>.</summary>
    abstract Availability: operation: SubtleOperation * algorithm: string -> CryptoOutcome<unit>

/// <summary>Argument validation, algorithm names and outcomes shared by crypto providers.</summary>
module CryptoContract =
    /// <summary>The largest byte length of a getRandomValues call.</summary>
    [<Literal>]
    let RandomValuesQuota = 65536

    /// <summary>The bytes an element of <c>kind</c> takes.</summary>
    let elementSize (kind: RandomValuesKind) =
        match kind with
        | RandomValuesKind.Int8
        | RandomValuesKind.Uint8
        | RandomValuesKind.Uint8Clamped
        | RandomValuesKind.DataView -> 1
        | RandomValuesKind.Int16
        | RandomValuesKind.Uint16
        | RandomValuesKind.Float16 -> 2
        | RandomValuesKind.Int32
        | RandomValuesKind.Uint32
        | RandomValuesKind.Float32 -> 4
        | RandomValuesKind.Float64
        | RandomValuesKind.BigInt64
        | RandomValuesKind.BigUint64 -> 8
        | _ -> raise (ArgumentOutOfRangeException ("kind", "The kind is not a RandomValuesKind."))

    /// <summary>Whether getRandomValues fills a view of <c>kind</c>: the integer and BigInt typed arrays.</summary>
    let fillsKind (kind: RandomValuesKind) =
        elementSize kind |> ignore

        match kind with
        | RandomValuesKind.Float16
        | RandomValuesKind.Float32
        | RandomValuesKind.Float64
        | RandomValuesKind.DataView -> false
        | _ -> true

    /// <summary>Raises ArgumentNullException or ArgumentOutOfRangeException unless the range lies inside <c>bytes</c>.</summary>
    let range (bytes: byte array) (offset: int) (count: int) =
        if isNull bytes then
            nullArg "bytes"

        if offset < 0 || offset > bytes.Length then
            raise (ArgumentOutOfRangeException ("offset"))

        if count < 0 || count > bytes.Length - offset then
            raise (ArgumentOutOfRangeException ("count"))

    /// <summary>
    /// Checks a getRandomValues call in Chromium's order: the range, the kind (TypeMismatchError), the quota
    /// (QuotaExceededError) and the element size (RangeError).
    /// </summary>
    let randomValues (kind: RandomValuesKind) (bytes: byte array) (offset: int) (count: int) =
        range bytes offset count
        let size = elementSize kind

        if not (fillsKind kind) then
            raise (CryptoException ("TypeMismatchError", "The view is not an integer typed array."))

        if count > RandomValuesQuota then
            raise (CryptoException ("QuotaExceededError", $"The byte length {count} exceeds {RandomValuesQuota}."))

        if count % size <> 0 then
            raise (CryptoException ("RangeError", "The byte length is not a multiple of the element size."))

    /// <summary>The SHA algorithm named <c>name</c>, compared case-insensitively, or None.</summary>
    let digestAlgorithm (name: string) =
        if isNull name then
            nullArg "algorithm"

        match name.ToUpperInvariant () with
        | "SHA-1" -> Some DigestAlgorithm.Sha1
        | "SHA-256" -> Some DigestAlgorithm.Sha256
        | "SHA-384" -> Some DigestAlgorithm.Sha384
        | "SHA-512" -> Some DigestAlgorithm.Sha512
        | _ -> None

    /// <summary>The registered WebCrypto name of <c>algorithm</c>.</summary>
    let digestName (algorithm: DigestAlgorithm) =
        match algorithm with
        | DigestAlgorithm.Sha1 -> "SHA-1"
        | DigestAlgorithm.Sha256 -> "SHA-256"
        | DigestAlgorithm.Sha384 -> "SHA-384"
        | DigestAlgorithm.Sha512 -> "SHA-512"

    /// <summary>The byte length of a digest of <c>algorithm</c>.</summary>
    let digestLength (algorithm: DigestAlgorithm) =
        match algorithm with
        | DigestAlgorithm.Sha1 -> 20
        | DigestAlgorithm.Sha256 -> 32
        | DigestAlgorithm.Sha384 -> 48
        | DigestAlgorithm.Sha512 -> 64

    /// <summary>The Unsupported outcome of a digest of an algorithm outside the SHA family.</summary>
    let unsupportedDigest (name: string) : CryptoOutcome<'T> =
        CryptoOutcome.Unsupported $"SubtleCrypto.digest supports SHA-1, SHA-256, SHA-384 and SHA-512, not '{name}'."

    /// <summary>The SubtleCrypto method name of <c>operation</c>, such as <c>digest</c> or <c>generateKey</c>.</summary>
    let operationName (operation: SubtleOperation) =
        match operation with
        | SubtleOperation.Digest -> "digest"
        | SubtleOperation.Encrypt -> "encrypt"
        | SubtleOperation.Decrypt -> "decrypt"
        | SubtleOperation.Sign -> "sign"
        | SubtleOperation.Verify -> "verify"
        | SubtleOperation.GenerateKey -> "generateKey"
        | SubtleOperation.DeriveKey -> "deriveKey"
        | SubtleOperation.DeriveBits -> "deriveBits"
        | SubtleOperation.ImportKey -> "importKey"
        | SubtleOperation.ExportKey -> "exportKey"
        | SubtleOperation.WrapKey -> "wrapKey"
        | SubtleOperation.UnwrapKey -> "unwrapKey"

    /// <summary>
    /// The availability every provider shares: digest of a SHA algorithm, and Unsupported for every other operation
    /// and algorithm.
    /// </summary>
    let availability (operation: SubtleOperation) (algorithm: string) : CryptoOutcome<unit> =
        if isNull algorithm then
            nullArg "algorithm"

        match operation, digestAlgorithm algorithm with
        | SubtleOperation.Digest, Some _ -> CryptoOutcome.Value ()
        | SubtleOperation.Digest, None -> unsupportedDigest algorithm
        | _ -> CryptoOutcome.Unsupported $"SubtleCrypto.{operationName operation} is not implemented for '{algorithm}'."
