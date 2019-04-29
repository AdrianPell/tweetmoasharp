using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Hammock;
using Hammock.Serialization;
using Hammock.Web;

#if PLATFORM_SUPPORTS_ASYNC_AWAIT
using System.Threading.Tasks;
#endif

#if SILVERLIGHT
using Hammock.Silverlight.Compat;
#endif

namespace TweetSharp
{
	/// <summary>
	/// Defines a contract for a <see cref="TwitterService" /> implementation.
	/// </summary>
	/// <seealso href="http://dev.twitter.com/doc" />
	public partial class TwitterService : TwitterServiceBase
	{
        private RestClient _client;

        private RestClient _uploadMediaClient;

        public TwitterService(TwitterClientInfo info) : base(info) { }

        public TwitterService(string consumerKey, string consumerSecret) : base(consumerKey, consumerSecret) { }

		public TwitterService(string consumerKey, string consumerSecret, ISerializer serializer, IDeserializer deserializer)
				: base(consumerKey, consumerSecret, serializer, deserializer)
		{
		}

		public TwitterService(string consumerKey, string consumerSecret, string proxy) : base(consumerKey, consumerSecret, proxy)
		{
		}

		public TwitterService(string consumerKey, string consumerSecret, string token, string tokenSecret)
            : base(consumerKey, consumerSecret, token, tokenSecret)
		{
		}

		public TwitterService(string consumerKey, string consumerSecret, string token, string tokenSecret, ISerializer serializer, IDeserializer deserializer)
				 : base(consumerKey, consumerSecret, token, tokenSecret, serializer, deserializer)
		{
		}

		public TwitterService(ISerializer serializer = null, IDeserializer deserializer = null, string proxy = null)
            :base(serializer, deserializer, proxy)
		{
		}

        protected override IEnumerable<RestClient> InitializeClients(ISerializer serializer, IDeserializer deserializer)
        {
            var jsonSerializer = new JsonSerializer();

            _client = new RestClient
            {
                Authority = Globals.Authority,
                QueryHandling = QueryHandling.AppendToParameters,
                VersionPath = "1.1",
                Serializer = serializer ?? jsonSerializer,
                Deserializer = deserializer ?? jsonSerializer,
                DecompressionMethods = DecompressionMethods.GZip,
                GetErrorResponseEntityType = (request, @base) => typeof(TwitterErrors),
                Proxy = Proxy,
#if !SILVERLIGHT && !WINRT
                FollowRedirects = true,
#endif
#if SILVERLIGHT
                HasElevatedPermissions = true
#endif
            };
            yield return _client;

            _uploadMediaClient = new RestClient
            {
                Authority = Globals.MediaUploadAuthority,
                QueryHandling = QueryHandling.AppendToParameters,
                VersionPath = "1.1",
                Serializer = serializer ?? jsonSerializer,
                Deserializer = deserializer ?? jsonSerializer,
                DecompressionMethods = DecompressionMethods.GZip,
                GetErrorResponseEntityType = (request, @base) => typeof(TwitterErrors),
                Proxy = Proxy,
#if !SILVERLIGHT && !WINRT
                FollowRedirects = true,
#endif
#if SILVERLIGHT
                HasElevatedPermissions = true
#endif
            };
            yield return _uploadMediaClient;
        }

        protected override void InitializeService()
		{
			IncludeEntities = true;
			IncludeRetweets = true;
			TweetMode = TweetSharp.TweetMode.Compatibility;
		}
	}
}