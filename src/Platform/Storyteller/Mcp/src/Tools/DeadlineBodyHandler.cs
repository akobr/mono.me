namespace _42.Platform.Storyteller.Mcp.Tools;

// Ties the body of every answer of the API to the deadline of the tool call (StorytellerCallFilter.CurrentCall). The
// generated SDK reads a body after its headers and deserializes it synchronously, without a token, so a body that stalls
// would hold the tool call past its deadline. Here the body's stream is closed when the deadline fires, and a read that
// was waiting, or one that comes later, throws an OperationCanceledException of the call's token.
internal sealed class DeadlineBodyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        var call = StorytellerCallFilter.CurrentCall;

        if (call.CanBeCanceled && response.Content is { } content)
        {
            response.Content = new DeadlineContent(content, call);
        }

        return response;
    }

    private sealed class DeadlineContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly CancellationToken _call;

        public DeadlineContent(HttpContent inner, CancellationToken call)
        {
            _inner = inner;
            _call = call;

            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            return new DeadlineStream(await _inner.ReadAsStreamAsync(cancellationToken), _call);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return CreateContentReadStreamAsync(CancellationToken.None);
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            await using var body = await CreateContentReadStreamAsync(cancellationToken);
            await body.CopyToAsync(stream, cancellationToken);
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            return SerializeToStreamAsync(stream, context, CancellationToken.None);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DeadlineStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _call;
        private readonly CancellationTokenRegistration _registration;

        public DeadlineStream(Stream inner, CancellationToken call)
        {
            _inner = inner;
            _call = call;

            // Closing the stream ends a read that waits for bytes that do not come.
            _registration = call.Register(static state => ((Stream)state!).Dispose(), inner);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _call.ThrowIfCancellationRequested();

            try
            {
                return _inner.Read(buffer, offset, count);
            }
            catch (Exception exception) when (_call.IsCancellationRequested && exception is not OperationCanceledException)
            {
                throw new OperationCanceledException("The deadline of the tool call ended the read of the answer.", exception, _call);
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _call.ThrowIfCancellationRequested();

            try
            {
                return await _inner.ReadAsync(buffer, cancellationToken);
            }
            catch (Exception exception) when (_call.IsCancellationRequested && exception is not OperationCanceledException)
            {
                throw new OperationCanceledException("The deadline of the tool call ended the read of the answer.", exception, _call);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _registration.Dispose();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
