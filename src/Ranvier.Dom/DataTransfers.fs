namespace Ranvier.Dom

open System

/// <summary>Constructs DataTransfer objects in one document.</summary>
/// <remarks>
/// A constructed DataTransfer is read/write and outlives the service that created it. The DataTransfer of a drag,
/// clipboard or input event follows the event's store mode: dragenter, dragover and dragleave read only its types;
/// drop and paste read its data and files; copy and cut write it; every one reads empty once its event is over.
/// </remarks>
#if FABLE_COMPILER
[<Fable.Core.Mangle>]
#endif
type DataTransferService =
    inherit IDisposable
    /// <summary>An empty read/write DataTransfer, as <c>new DataTransfer()</c> creates.</summary>
    abstract Create: unit -> DataTransfer
