using System.Collections.Generic;
using Elasticsearch.Net;
using NLog.Layouts;

namespace NLog.Targets.ElasticSearch
{
    public interface IElasticSearchSerializedTarget
	{
        /// <summary>
        /// Gets or sets a connection string name to retrieve the Uri from.
        /// 
        /// Use as an alternative to Uri
        /// </summary>
        string ConnectionStringName { get; set; }

        /// <summary>
        /// Gets or sets the elasticsearch uri, can be multiple comma separated.
        /// </summary>
        string Uri { get; set; }

        /// <summary>
        /// Set it to true if ElasticSearch uses BasicAuth
        /// </summary>
        bool RequireAuth { get; set; }

        /// <summary>
        /// Username for basic auth
        /// </summary>
        string Username { get; set; }

        /// <summary>
        /// Password for basic auth
        /// </summary>
        string Password { get; set; }

        /// <summary>
        /// Set it to true to disable proxy detection
        /// </summary>
        bool DisableAutomaticProxyDetection { get; set; }

        /// <summary>
        /// Gets or sets the name of the elasticsearch index to write to.
        /// </summary>
        Layout Index { get; set; }

        /// <summary>
        /// Gets or sets the document type for the elasticsearch index.
        /// </summary>
        Layout DocumentType { get; set; }

		/// <summary>
		/// DANGEROUS, NEVER USE IN PRODUCTION ENVIRONMENT. Gets or sets whether the connection should accept all certificates, useful for test environments.
		/// </summary>
		bool DangerousAcceptAllCertificates { get; set; }
    }
}