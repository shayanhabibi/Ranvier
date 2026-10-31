namespace Ranvier.Dom

open System
open Fable.Core
open Fable.Core.JsInterop

/// <summary>Form submission reporting in the active document realm.</summary>
/// <remarks>
/// The provider cancels each submit event that reaches the Window uncancelled, and replaces
/// <c>HTMLFormElement.prototype.submit</c> in the realm until disposal. An application listener that cancels a
/// submission is registered on the form, the document or the Window before the service. The provider encodes the
/// entry list as the HTML form submission algorithm does, in UTF-8.
/// </remarks>
module BrowserFormSubmission =
    [<Emit("$0.defaultView != null && $0.defaultView.document === $0 && typeof $0.defaultView.FormData === 'function'")>]
    let private current (document: Document) : bool = jsNative

    [<Emit("$0.defaultView.__ranvierFormSubmission === true")>]
    let private installed (document: Document) : bool = jsNative

    // Returns the function that removes the listener, restores HTMLFormElement.prototype.submit and drops the pending
    // submissions.
    [<Emit("""(function (document, deliver) {
  var window = document.defaultView;
  var pending = new Map();
  var next = 0;
  var disposed = false;
  var encoder = new TextEncoder();
  var normalize = function (text) { return text.replace(/\r\n|\r|\n/g, '\r\n'); };
  var has = function (submitter, name) { return submitter != null && submitter.hasAttribute(name); };
  var methodOf = function (form, submitter) {
    return (has(submitter, 'formmethod') ? submitter.formMethod : form.method).toLowerCase();
  };
  var text = function (value) { return typeof value === 'string' ? value : value.name; };
  var escapeName = function (name) { return name.replace(/\n/g, '%0A').replace(/\r/g, '%0D').replace(/"/g, '%22'); };
  var urlencoded = function (entries) {
    return new URLSearchParams(entries.map(function (entry) { return [entry[0], text(entry[1])]; })).toString();
  };
  var multipart = function (entries, boundary) {
    var parts = entries.map(function (entry) {
      var head = '--' + boundary + '\r\nContent-Disposition: form-data; name="' + escapeName(entry[0]) + '"';
      if (typeof entry[1] === 'string')
        return head + '\r\n\r\n' + entry[1] + '\r\n';
      return head + '; filename="' + escapeName(entry[1].name) + '"\r\nContent-Type: '
        + (entry[1].type || 'application/octet-stream') + '\r\n\r\n\r\n';
    });
    return parts.join('') + '--' + boundary + '--\r\n';
  };
  var plan = function (form, submitter) {
    var data = new window.FormData(form, submitter || undefined);
    var entries = Array.from(data.entries()).map(function (entry) {
      return [normalize(entry[0]), typeof entry[1] === 'string' ? normalize(entry[1]) : entry[1]];
    });
    var post = methodOf(form, submitter) === 'post';
    var action = has(submitter, 'formaction') ? submitter.formAction : form.action;
    var enctype = has(submitter, 'formenctype') ? submitter.formEnctype : form.enctype;
    var base = document.querySelector('base[target]');
    var target = has(submitter, 'formtarget') ? submitter.getAttribute('formtarget')
      : form.hasAttribute('target') ? form.getAttribute('target')
      : base != null ? base.getAttribute('target') : '';
    var url = action;
    var contentType = null;
    var body = new Uint8Array(0);
    if (!post) {
      var hash = action.indexOf('#');
      var rest = hash < 0 ? action : action.slice(0, hash);
      var query = rest.indexOf('?');
      url = (query < 0 ? rest : rest.slice(0, query)) + '?' + urlencoded(entries) + (hash < 0 ? '' : action.slice(hash));
    } else if (enctype === 'multipart/form-data') {
      var boundary = '----RanvierFormBoundary' + Math.random().toString(36).slice(2, 10) + Math.random().toString(36).slice(2, 10);
      contentType = 'multipart/form-data; boundary=' + boundary;
      body = encoder.encode(multipart(entries, boundary));
    } else if (enctype === 'text/plain') {
      contentType = 'text/plain';
      body = encoder.encode(entries.map(function (entry) { return entry[0] + '=' + text(entry[1]) + '\r\n'; }).join(''));
    } else {
      contentType = 'application/x-www-form-urlencoded';
      body = encoder.encode(urlencoded(entries));
    }
    var submission = { method: post ? 'POST' : 'GET', url: url, target: target, contentType: contentType, body: body,
                       form: form, submitter: submitter || null };
    var token = ++next;
    pending.set(form, token);
    window.setTimeout(function () {
      if (disposed || pending.get(form) !== token)
        return;
      pending.delete(form);
      deliver(submission);
    }, 0);
  };
  var onSubmit = function (event) {
    var form = event.target;
    if (event.defaultPrevented || !(form instanceof window.HTMLFormElement) || methodOf(form, event.submitter) === 'dialog')
      return;
    event.preventDefault();
    plan(form, event.submitter);
  };
  var original = window.HTMLFormElement.prototype.submit;
  var patched = function () {
    if (!this.isConnected || methodOf(this, null) === 'dialog')
      return original.call(this);
    plan(this, null);
  };
  window.addEventListener('submit', onSubmit);
  window.HTMLFormElement.prototype.submit = patched;
  window.__ranvierFormSubmission = true;
  return function () {
    disposed = true;
    pending.clear();
    window.removeEventListener('submit', onSubmit);
    if (window.HTMLFormElement.prototype.submit === patched)
      window.HTMLFormElement.prototype.submit = original;
    delete window.__ranvierFormSubmission;
  };
})($0, $1)""")>]
    let private install (document: Document) (deliver: obj -> unit) : unit -> unit = jsNative

    let private submission (raw: obj) : FormSubmission =
        {
            Method = raw?method
            Url = raw?url
            Target = raw?target
            ContentType =
                if isNull raw?contentType then
                    None
                else
                    Some (unbox<string> raw?contentType)
            Body = raw?body
            Form = raw?form
            Submitter =
                if isNull raw?submitter then
                    None
                else
                    Some (unbox<HTMLElement> raw?submitter)
        }

    /// <summary>A FormSubmission service for <c>document</c>'s realm.</summary>
    /// <remarks>
    /// Raises NotSupportedException for a document without an active Window, and InvalidOperationException while
    /// another service of the realm is undisposed.
    /// </remarks>
    let forDocument (document: Document) : FormSubmissionService =
        if isNull (box document) then
            nullArg "document"

        if not (current document) then
            raise (NotSupportedException "Form submission reporting requires an active document Window")

        if installed document then
            raise (InvalidOperationException "Another FormSubmission service of this realm is undisposed")

        let handlers = FormSubmissionContract.Handlers ()
        let mutable disposed = false
        let uninstall = install document (fun raw -> handlers.Deliver (submission raw))

        { new FormSubmissionService with
            member _.Subscribe handler =
                if disposed then
                    raise (ObjectDisposedException (nameof FormSubmissionService))

                handlers.Add (FormSubmissionContract.handler handler)

            member _.Dispose() =
                if not disposed then
                    disposed <- true
                    uninstall ()
                    handlers.Clear ()
        }
