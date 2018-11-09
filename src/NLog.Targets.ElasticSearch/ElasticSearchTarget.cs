﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Elasticsearch.Net;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using Newtonsoft.Json;

namespace NLog.Targets.ElasticSearch
{
    [Target("ElasticSearch")]
    public class ElasticSearchTarget : TargetWithLayout, IElasticSearchTarget
    {
        private IElasticLowLevelClient _client;
        private List<string> _excludedProperties = new List<string>(new[] { "CallerMemberName", "CallerFilePath", "CallerLineNumber", "MachineName", "ThreadId" });
        private readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

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
        /// Gets or sets whether to include all properties of the log event in the document
        /// </summary>
        public bool IncludeAllProperties { get; set; }

        /// <summary>
        /// Gets or sets a comma separated list of excluded properties when setting <see cref="IElasticSearchTarget.IncludeAllProperties"/>
        /// </summary>
        public string ExcludedProperties { get; set; }

        /// <summary>
        /// Gets or sets the document type for the elasticsearch index.
        /// </summary>
        [RequiredParameter]
        public Layout DocumentType { get; set; } = "logevent";

        /// <summary>
        /// Gets or sets a list of additional fields to add to the elasticsearch document.
        /// </summary>
        [ArrayParameter(typeof(Field), "field")]
        public IList<Field> Fields { get; set; } = new List<Field>();

        /// <summary>
        /// Gets or sets an alternative serializer for the elasticsearch client to use.
        /// </summary>
        public IElasticsearchSerializer ElasticsearchSerializer { get; set; }

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

		public ElasticSearchTarget()
        {
            Name = "ElasticSearch";
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            var uri = ConnectionStringName.GetConnectionString() ?? (_uri?.Render(LogEventInfo.CreateNullEvent())) ?? string.Empty;
            var nodes = uri.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(url => new Uri(url));
            var connectionPool = new StaticConnectionPool(nodes);

            var config = new ConnectionConfiguration(connectionPool);

            if (ElasticsearchSerializer != null)
                config = new ConnectionConfiguration(connectionPool, ElasticsearchSerializer);

            if (RequireAuth)
                config.BasicAuthentication(Username, Password);

            if (DisableAutomaticProxyDetection)
                config.DisableAutomaticProxyDetection();

			if (DangerousAcceptAllCertificates)
				config.ServerCertificateValidationCallback(CertificateValidations.AllowAll);

            _client = new ElasticLowLevelClient(config);


            if (!string.IsNullOrEmpty(ExcludedProperties))
                _excludedProperties = ExcludedProperties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
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
            var payload = new List<object>(logEvents.Count);

            foreach (var ev in logEvents)
            {
                var logEvent = ev.LogEvent;

                var document = new Dictionary<string, object>
                {
                    {"@timestamp", logEvent.TimeStamp},
                    {"level", logEvent.Level.Name},
                    {"message", Layout.Render(logEvent)}
                };

                if (logEvent.Exception != null)
                {
                    var jsonString = JsonConvert.SerializeObject(logEvent.Exception, _jsonSerializerSettings);
                    var ex = JsonConvert.DeserializeObject<ExpandoObject>(jsonString);
                    document.Add("exception", ex.ReplaceDotInKeys());
                }

                foreach (var field in Fields)
                {
                    var renderedField = field.Layout.Render(logEvent);
                    if (!string.IsNullOrWhiteSpace(renderedField))
                        document[field.Name] = renderedField.ToSystemType(field.LayoutType, logEvent.FormatProvider);
                }

                if (IncludeAllProperties && logEvent.HasProperties)
                {
                    foreach (var p in logEvent.Properties)
                    {
                        var propertyKey = p.Key.ToString();
                        if (_excludedProperties.Contains(propertyKey))
                            continue;
                        if (document.ContainsKey(propertyKey))
                            continue;

                        document[propertyKey] = p.Value;
                    }
                }

                var index = Index.Render(logEvent).ToLowerInvariant();
                var type = DocumentType.Render(logEvent);

                payload.Add(new { index = new { _index = index, _type = type } });
                payload.Add(document);
            }

            return PostData.MultiJson(payload);
        }
    }
}
