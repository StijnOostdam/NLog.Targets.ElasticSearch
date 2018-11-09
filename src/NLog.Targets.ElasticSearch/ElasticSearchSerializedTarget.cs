using System;
using System.Collections.Generic;
using System.Linq;
using Elasticsearch.Net;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;

namespace NLog.Targets.ElasticSearch
{
    [Target("ElasticSearchSerialized")]
    public class ElasticSearchSerializedTarget : TargetWithLayout, IElasticSearchSerializedTarget
	{
        private IElasticLowLevelClient _client;

        /// <summary>
        /// Gets or sets a connection string name to retrieve the Uri from.
        /// 
        /// Use as an alternative to Uri
        /// </summary>
        public string ConnectionStringName { get; set; }

        /// <summary>
        /// Gets or sets the elasticsearch uri, can be multiple comma separated.
        /// </summary>
        public string Uri { get => (_uri as SimpleLayout)?.Text; set => _uri = value ?? string.Empty; }
        private Layout _uri = "http://localhost:9200";

        /// <summary>
        /// Set it to true if ElasticSearch uses BasicAuth
        /// </summary>
        public bool RequireAuth { get; set; }

        /// <summary>
        /// Username for basic auth
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password for basic auth
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Set it to true to disable proxy detection
        /// </summary>
        public bool DisableAutomaticProxyDetection { get; set; }

        /// <summary>
        /// Gets or sets the name of the elasticsearch index to write to.
        /// </summary>
        public Layout Index { get; set; } = "logstash-${date:format=yyyy.MM.dd}";

        /// <summary>
        /// Gets or sets the document type for the elasticsearch index.
        /// </summary>
        [RequiredParameter]
        public Layout DocumentType { get; set; } = "logevent";

        /// <summary>
        /// Gets or sets if exceptions will be rethrown.
        /// 
        /// Set it to true if ElasticSearchTarget target is used within FallbackGroup target (https://github.com/NLog/NLog/wiki/FallbackGroup-target).
        /// </summary>
        public bool ThrowExceptions { get; set; }

		/// <summary>
		/// DANGEROUS, NEVER USE IN PRODUCTION ENVIRONMENT. Gets or sets whether the connection should accept all certificates, useful for test environments.
		/// </summary>
		public bool DangerousAcceptAllCertificates { get; set; } = false;

		public ElasticSearchSerializedTarget()
        {
            Name = "ElasticSearchSerialized";
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            var uri = ConnectionStringName.GetConnectionString() ?? (_uri?.Render(LogEventInfo.CreateNullEvent())) ?? string.Empty;
            var nodes = uri.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(url => new Uri(url));
            var connectionPool = new StaticConnectionPool(nodes);

            var config = new ConnectionConfiguration(connectionPool);

            if (RequireAuth)
                config.BasicAuthentication(Username, Password);

            if (DisableAutomaticProxyDetection)
                config.DisableAutomaticProxyDetection();

			if (DangerousAcceptAllCertificates)
				config.ServerCertificateValidationCallback(CertificateValidations.AllowAll);

            _client = new ElasticLowLevelClient(config);
        }

        protected override void Write(AsyncLogEventInfo logEvent)
        {
            SendBatch(new[] { logEvent });
        }

        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            SendBatch(logEvents);
        }

        private void SendBatch(ICollection<AsyncLogEventInfo> logEvents)
        {
            try
            {
                var payload = FormPayload(logEvents);

                var result = _client.Bulk<BytesResponse>(payload);

                if (!result.Success)
                {
                    var errorMessage = result.OriginalException?.Message ?? "No error message. Enable Trace logging for more information.";
                    InternalLogger.Error($"ElasticSearch: Failed to send log messages. status={result.HttpStatusCode}, message=\"{errorMessage}\"");
                    InternalLogger.Trace($"ElasticSearch: Failed to send log messages. result={result}");

                    if (result.OriginalException != null)
                        throw result.OriginalException;
                }

                foreach (var ev in logEvents)
                {
                    ev.Continuation(null);
                }
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aggregateException)
                {
                    var flattenException = aggregateException.Flatten();
                    if (flattenException.InnerExceptions.Count == 1)
                        InternalLogger.Error(flattenException.InnerExceptions[0], "ElasticSearch: Error while sending log messages");
                    else
                        InternalLogger.Error(flattenException, "ElasticSearch: Error while sending log messages");
                }
                else
                {
                    InternalLogger.Error(ex, "ElasticSearch: Error while sending log messages");
                }

                foreach(var ev in logEvents)
                {
                    ev.Continuation(ex);
                }
            }
        }

        private PostData FormPayload(ICollection<AsyncLogEventInfo> logEvents)
        {
            var payload = new List<object>(logEvents.Count * 2);

            foreach (var ev in logEvents)
            {
                var logEvent = ev.LogEvent;

                var index = Index.Render(logEvent).ToLowerInvariant();
                var type = DocumentType.Render(logEvent);

                payload.Add(new { index = new { _index = index, _type = type } });
                payload.Add(ev.LogEvent.Message);
            }

            return PostData.MultiJson(payload);
        }
    }
}
