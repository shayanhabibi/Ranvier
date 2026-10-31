namespace Ranvier.Dom

open System
open Fable.Core

/// <summary>crypto.getRandomValues, crypto.randomUUID and crypto.subtle.digest of the active document realm.</summary>
module BrowserCrypto =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.crypto?.getRandomValues === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView")>]
    let private realm (document: Document) : obj = jsNative

    [<Emit("typeof $0.crypto.randomUUID === 'function'")>]
    let private hasRandomUUID (realm: obj) : bool = jsNative

    [<Emit("typeof $0.crypto.subtle?.digest === 'function'")>]
    let private hasDigest (realm: obj) : bool = jsNative

    [<Emit("$0.crypto.randomUUID()")>]
    let private randomUUID (realm: obj) : string = jsNative

    // A view of the realm's typed array type over a copy of the range; the copy returns to the range on success.
    [<Emit("(() => { const copy = $3.slice($4, $4 + $5); const C = $0[$1]; const view = $1 === 'DataView' ? new C(copy.buffer) : new C(copy.buffer, 0, $5 / $2); $0.crypto.getRandomValues(view); $3.set(copy, $4); })()")>]
    let private fill (realm: obj) (constructor: string) (size: int) (bytes: byte array) (offset: int) (count: int) : unit = jsNative

    [<Emit("$0.crypto.subtle.digest($1, $2.slice($3, $3 + $4)).then(b => new Uint8Array(b))")>]
    let private digest (realm: obj) (name: string) (data: byte array) (offset: int) (count: int) : JS.Promise<byte array> = jsNative

    [<Emit("String($0?.name ?? 'Error')")>]
    let private errorName (error: obj) : string = jsNative

    [<Emit("String($0?.message ?? $0)")>]
    let private errorMessage (error: obj) : string = jsNative

    let private constructorName (kind: RandomValuesKind) =
        match kind with
        | RandomValuesKind.Int8 -> "Int8Array"
        | RandomValuesKind.Uint8 -> "Uint8Array"
        | RandomValuesKind.Uint8Clamped -> "Uint8ClampedArray"
        | RandomValuesKind.Int16 -> "Int16Array"
        | RandomValuesKind.Uint16 -> "Uint16Array"
        | RandomValuesKind.Int32 -> "Int32Array"
        | RandomValuesKind.Uint32 -> "Uint32Array"
        | RandomValuesKind.Float16 -> "Float16Array"
        | RandomValuesKind.Float32 -> "Float32Array"
        | RandomValuesKind.Float64 -> "Float64Array"
        | RandomValuesKind.BigInt64 -> "BigInt64Array"
        | RandomValuesKind.BigUint64 -> "BigUint64Array"
        | _ -> "DataView"

    /// <summary>A crypto service for <c>document</c>'s realm.</summary>
    /// <remarks>
    /// Raises NotSupportedException without an active document Window. In an insecure context randomUUID and
    /// digest read as Unsupported, since the realm has no crypto.randomUUID or crypto.subtle.
    /// </remarks>
    let forDocument (document: Document) : CryptoService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Crypto requires an active document Window")

        let window = realm document
        let disposed = ref false

        let check () =
            if disposed.Value then
                raise (ObjectDisposedException (nameof CryptoService))

            if not (current document) then
                raise (InvalidOperationException "The crypto document is no longer active")

        { new CryptoService with
            member _.GetRandomValues(kind, bytes, offset, count) =
                check ()
                CryptoContract.randomValues kind bytes offset count

                try
                    fill window (constructorName kind) (CryptoContract.elementSize kind) bytes offset count
                with error ->
                    raise (CryptoException (errorName error, errorMessage error))

            member _.RandomUUID() =
                check ()

                if hasRandomUUID window then
                    CryptoOutcome.Value (randomUUID window)
                else
                    CryptoOutcome.Unsupported "crypto.randomUUID requires a secure context."

            member _.Digest(algorithm, data, offset, count) =
                check ()
                CryptoContract.range data offset count

                match CryptoContract.digestAlgorithm algorithm with
                | None -> async { return CryptoContract.unsupportedDigest algorithm }
                | Some _ when not (hasDigest window) -> async { return CryptoOutcome.Unsupported "crypto.subtle requires a secure context." }
                | Some known ->
                    let pending = digest window (CryptoContract.digestName known) data offset count

                    async {
                        let! bytes = Async.AwaitPromise pending
                        return CryptoOutcome.Value bytes
                    }

            member _.Availability(operation, algorithm) =
                check ()

                match CryptoContract.availability operation algorithm with
                | CryptoOutcome.Value () when not (hasDigest window) -> CryptoOutcome.Unsupported "crypto.subtle requires a secure context."
                | outcome -> outcome

            member _.Dispose() =
                disposed.Value <- true
        }
