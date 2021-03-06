﻿using Hammock;
using Hammock.Serialization;
using Hammock.Web;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

#if PLATFORM_SUPPORTS_ASYNC_AWAIT
using System.Threading.Tasks;
#endif

#if SILVERLIGHT
using Hammock.Silverlight.Compat;
#endif

namespace TweetSharp
{
    public abstract partial class TwitterServiceBase
    {
        public bool TraceEnabled { get; set; }
        public string Proxy { get; set; }
        public bool IncludeEntities { get; set; }
        public bool IncludeRetweets { get; set; }
        public string TweetMode { get; set; }

        private List<RestClient> Clients = new List<RestClient>();

        private string _consumerKey;
        private string _consumerSecret;
        private string _token;
        private string _tokenSecret;

        public TwitterServiceBase(TwitterClientInfo info) : this()
        {
            _consumerKey = info.ConsumerKey;
            _consumerSecret = info.ConsumerSecret;
            IncludeEntities = info.IncludeEntities;
            IncludeRetweets = info.IncludeRetweets;
            TweetMode = info.TweetMode;

            _info = info;
        }

        public TwitterServiceBase(string consumerKey, string consumerSecret) : this()
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
        }

        public TwitterServiceBase(string consumerKey, string consumerSecret, ISerializer serializer, IDeserializer deserializer)
                : this(serializer, deserializer)
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
        }

        public TwitterServiceBase(string consumerKey, string consumerSecret, string proxy) : this(proxy: proxy)
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
        }

        public TwitterServiceBase(string consumerKey, string consumerSecret, string token, string tokenSecret) : this()
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
            _token = token;
            _tokenSecret = tokenSecret;
        }

        public TwitterServiceBase(string consumerKey, string consumerSecret, string token, string tokenSecret, ISerializer serializer, IDeserializer deserializer)
                 : this(serializer, deserializer)
        {
            _consumerKey = consumerKey;
            _consumerSecret = consumerSecret;
            _token = token;
            _tokenSecret = tokenSecret;
        }

        public TwitterServiceBase(ISerializer serializer = null, IDeserializer deserializer = null, string proxy = null)
        {
            Proxy = proxy;
            FormatAsString = ".json";

            Clients.Add(InitializeOAuthClient());
            Clients.AddRange(InitializeClients(serializer, deserializer));
            UserAgent = "TweetSharp";
            InitializeService();
        }

        protected abstract IEnumerable<RestClient> InitializeClients(ISerializer serializer, IDeserializer deserializer);

        protected virtual void InitializeService()
        {
        }

        private string _userAgent;
        public string UserAgent
        {
            get => _userAgent;
            set
            {
                _userAgent = value;
                Clients.ForEach(c => c.UserAgent = value);
            }
        }

        private IDeserializer _deserializer;
        public IDeserializer Deserializer
        {
            get => _deserializer;
            set {
                _deserializer = value;
                Clients.ForEach(c => c.Deserializer = value);
            }
        }

        private ISerializer _serializer;
        public ISerializer Serializer
        {
            get => _serializer;
            set
            {
                _serializer = value;
                Clients.ForEach(c => c.Serializer = value);
            }
        }

#if !WINDOWS_PHONE
        protected void SetResponse(RestResponseBase response)
        {
            Response = new TwitterResponse(response);
        }
#endif

#if !SILVERLIGHT && !WINRT && !WINDOWS_PHONE
        static TwitterServiceBase()
        {
            ServicePointManager.Expect100Continue = false;
        }
#endif

#if !WINDOWS_PHONE
        public virtual TwitterResponse Response { get; private set; }
#endif

        protected TwitterClientInfo _info;

        private void SetTwitterClientInfo(RestBase request)
        {
            if (_info == null) return;
            if (!_info.ClientName.IsNullOrBlank())
            {
                request.AddHeader("X-Twitter-Name", _info.ClientName);
                request.UserAgent = _info.ClientName;
            }
            if (!_info.ClientVersion.IsNullOrBlank())
            {
                request.AddHeader("X-Twitter-Version", _info.ClientVersion);
            }
            if (!_info.ClientUrl.IsNullOrBlank())
            {
                request.AddHeader("X-Twitter-URL", _info.ClientUrl);
            }
        }


        private readonly Func<RestRequest> _noAuthQuery
                = () =>
                {
                    var request = new RestRequest();
                    return request;
                };

        public T Deserialize<T>(ITwitterModel model) where T : ITwitterModel
        {
            return Deserialize<T>(model.RawSource);
        }

        public T Deserialize<T>(string content)
        {
            var response = new RestResponse<T> { StatusCode = HttpStatusCode.OK };
            response.SetContent(content);
            return Deserializer.Deserialize<T>(response);
        }

        private RestRequest PrepareHammockQuery(string path)
        {
            RestRequest request;
            //TFW - 2017-08-04 - No auth query if there's no consumer token
            //can't have user token without consumer token, but can have
            //consumer token without user token (for 'app only' auth).
            //Else path works fine when _token and _tokenSecret are null/empty.
            if (string.IsNullOrEmpty(_consumerKey) || string.IsNullOrEmpty(_consumerSecret))
            {
                request = _noAuthQuery.Invoke();
            }
            else
            {
                var args = new FunctionArguments
                {
                    ConsumerKey = _consumerKey,
                    ConsumerSecret = _consumerSecret,
                    Token = _token,
                    TokenSecret = _tokenSecret
                };
                request = _protectedResourceQuery.Invoke(args);
            }
            request.Path = path;

            SetTwitterClientInfo(request);

            // A little hacky, but these URLS have never changed
            if (path.Contains("account/update_profile_background_image") ||
                    path.Contains("account/update_profile_image"))
            {
                PrepareUpload(request, path);
            }

            request.TraceEnabled = TraceEnabled;
            return request;
        }

        private static void PrepareUpload(RestBase request, string path)
        {
            //account/update_profile_image.json?image=[FILE_PATH]&include_entities=1
            var startIndex = path.IndexOf("?image_path=", StringComparison.Ordinal) + 12;
            var endIndex = path.IndexOf("&", StringComparison.Ordinal);
            var uri = path.Substring(startIndex, endIndex - startIndex);
            path = path.Replace(string.Format("image_path={0}&", uri), "");
            request.Path = path;
            request.Method = WebMethod.Post;
#if !WINRT
            request.AddFile("image", Path.GetFileName(Uri.UnescapeDataString(uri)), Path.GetFullPath(Uri.UnescapeDataString(uri)), "multipart/form-data");
#else
					var fullPath = Uri.UnescapeDataString(uri);
					if (!System.IO.Path.IsPathRooted(fullPath)) //Best guess at how to create a 'full' path on WinRT where file access is restricted and all paths should be passed as 'full' versions anyway.
						fullPath = System.IO.Path.Combine(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, uri);
					request.AddFile("image", Path.GetFileName(Uri.UnescapeDataString(uri)), fullPath, "multipart/form-data");
#endif
        }

        internal string FormatAsString { get; set; }

        private string ResolveUrlSegments(string path, List<object> segments)
        {
            if (segments == null) throw new ArgumentNullException("segments");

            var cleansed = new List<object>();
            for (var i = 0; i < segments.Count; i++)
            {
                if (i == 0)
                {
                    cleansed.Add(segments[i]);
                }
                if (i > 0 && i % 2 == 0)
                {
                    var key = segments[i - 1];
                    var value = segments[i];
                    if (value != null)
                    {
                        if (cleansed.Count == 1 && key is string)
                        {
                            var keyString = key.ToString();
                            if (keyString.StartsWith("&"))
                            {
                                key = "?" + keyString.Substring(1);
                            }
                        }
                        cleansed.Add(key);
                        cleansed.Add(value);
                    }
                }
            }
            segments = cleansed;

            for (var i = 0; i < segments.Count; i++)
            {
                if (segments[i] is DateTime)
                {
                    segments[i] = ((DateTime)segments[i]).ToString("yyyy-MM-dd");
                }

                if (segments[i] is bool)
                {
                    var flag = (bool)segments[i];
                    segments[i] = flag ? "true" : "false";
                }

                if (segments[i] is double)
                {
                    segments[i] = ((double)segments[i]).ToString(CultureInfo.InvariantCulture);
                }

                if (segments[i] is decimal)
                {
                    segments[i] = ((decimal)segments[i]).ToString(CultureInfo.InvariantCulture);
                }

                if (segments[i] is float)
                {
                    segments[i] = ((float)segments[i]).ToString(CultureInfo.InvariantCulture);
                }

                if (segments[i] is TwitterListMode)
                {
                    segments[i] = segments[i].ToString().ToLowerInvariant();
                }

                if (segments[i] is IEnumerable && !(segments[i] is string))
                {
                    ResolveEnumerableUrlSegments(segments, i);
                }
            }

            path = PathHelpers.ReplaceUriTemplateTokens(segments, path);

            PathHelpers.EscapeDataContainingUrlSegments(segments);

            const string includeEntities = "include_entities";
            const string includeRetweets = "include_rts";
            const string tweetMode = "tweet_mode";

            if (IncludeEntities && !IsKeyAlreadySet(segments, includeEntities))
            {
                segments.Add(segments.Count() > 1 ? "&" + includeEntities + "=" : "?" + includeEntities + "=");
                segments.Add("1");
            }
            if (IncludeRetweets && !IsKeyAlreadySet(segments, includeRetweets))
            {
                segments.Add(segments.Count() > 1 ? "&" + includeRetweets + "=" : "?" + includeRetweets + "=");
                segments.Add("1");
            }
            if (!String.IsNullOrEmpty(TweetMode) && !IsKeyAlreadySet(segments, tweetMode))
            {
                segments.Add(segments.Count() > 1 ? "&" + tweetMode + "=" : "?" + tweetMode + "=");
                segments.Add(TweetMode);
            }

            segments.Insert(0, path);
#if !WINRT
            return string.Concat(segments.ToArray()).ToString(CultureInfo.InvariantCulture);
#else
						return string.Concat(segments.ToArray());
#endif
        }

        private static bool IsKeyAlreadySet(IList<object> segments, string key)
        {
            for (var i = 1; i < segments.Count; i++)
            {
                if (i % 2 != 1 || !(segments[i] is string)) continue;
                var segment = ((string)segments[i]).Trim(new[] { '&', '=', '?' });

                if (!segment.Contains(key)) continue;
                return true;
            }
            return false;
        }

        private static void ResolveEnumerableUrlSegments(IList<object> segments, int i)
        {
            // [DC] Enumerable segments will be typed, but we only care about string values
            var collection = (from object item in (IEnumerable)segments[i] select item.ToString()).ToList();
            var total = collection.Count();
            var sb = new StringBuilder();
            var count = 0;
            foreach (var item in collection)
            {
                sb.Append(item);
                if (count < total - 1)
                {
                    sb.Append(",");
                }
                count++;
            }
            segments[i] = sb.ToString();
        }

#if !WINDOWS_PHONE
        protected IAsyncResult WithHammock<T>(RestClient client, Action<T, TwitterResponse> action, string path) where T : class
        {
            var request = PrepareHammockQuery(path);

            return WithHammockImpl(client, request, action);
        }

        protected IAsyncResult WithHammock<T>(RestClient client, Action<T, TwitterResponse> action, string path, params object[] segments) where T : class
        {
            return WithHammock(client, action, ResolveUrlSegments(path, segments.ToList()));
        }

        protected IAsyncResult WithHammock<T>(RestClient client, WebMethod method, Action<T, TwitterResponse> action, string path) where T : class
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;

            return WithHammockImpl(client, request, action);
        }

        protected IAsyncResult WithHammock<T>(RestClient client, WebMethod method, Action<T, TwitterResponse> action, string path, byte[] bodyContent, string contentType) where T : class
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;
            request.AddPostContent(bodyContent);
            request.AddHeader("content-type", contentType);

            return WithHammockImpl(client, request, action);
        }

        protected IAsyncResult WithHammock<T>(RestClient client, WebMethod method, Action<T, TwitterResponse> action, MediaFile media, string path) where T : class
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;
            request.AddFile("media", media.FileName, media.Content);

            return WithHammockImpl(client, request, action);
        }

        protected IAsyncResult WithHammockNoResponse(RestClient client, WebMethod method, Action<TwitterResponse> action, MediaFile media, string path)
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;
            request.AddFile("media", media.FileName, media.Content);

            return WithHammockNoResponseImpl(client, request, action);
        }

        protected IAsyncResult WithHammock<T>(RestClient client, WebMethod method, Action<T, TwitterResponse> action, string path, params object[] segments) where T : class
        {
            return WithHammock(client, method, action, ResolveUrlSegments(path, segments.ToList()));
        }

        protected IAsyncResult WithHammockNoResponse(RestClient client, WebMethod method, Action<TwitterResponse> action, string path, params object[] segments)
        {
            var request = PrepareHammockQuery(ResolveUrlSegments(path, segments.ToList()));
            request.Method = method;

            return WithHammockNoResponseImpl(client, request, action);
        }

        protected IAsyncResult WithHammock<T>(RestClient client, WebMethod method, Action<T, TwitterResponse> action, string path, MediaFile media, params object[] segments) where T : class
        {
            return WithHammock(client, method, action, media, ResolveUrlSegments(path, segments.ToList()));
        }

        protected IAsyncResult WithHammockNoResponse(RestClient client, WebMethod method, Action<TwitterResponse> action, string path, MediaFile media, params object[] segments)
        {
            return WithHammockNoResponse(client, method, action, media, ResolveUrlSegments(path, segments.ToList()));
        }

        protected IAsyncResult WithHammockImpl<T>(RestClient client, RestRequest request, Action<T, TwitterResponse> action) where T : class
        {
            return client.BeginRequest(
                    request, new RestCallback<T>((req, response, state) =>
                    {
                        if (response == null)
                        {
                            return;
                        }
                        SetResponse(response);
                        var entity = response.ContentEntity;
                        action.Invoke(entity, new TwitterResponse(response));
                    }));
        }

        protected IAsyncResult WithHammockNoResponseImpl(RestClient client, RestRequest request, Action<TwitterResponse> action)
        {
            return client.BeginRequest(
                    request, new RestCallback((req, response, state) =>
                    {
                        if (response == null)
                        {
                            return;
                        }
                        SetResponse(response);
                        action.Invoke(new TwitterResponse(response));
                    }));
        }

        protected IAsyncResult BeginWithHammock<T>(RestClient client, WebMethod method, string path, params object[] segments)
        {
            path = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(path);
            request.Method = method;
            var result = client.BeginRequest<T>(request);
            return result;
        }

        protected IAsyncResult BeginWithHammock<T>(RestClient client, WebMethod method, string path, byte[] bodyContent, string contentType)
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;
            request.AddPostContent(bodyContent);
            request.AddHeader("Content-Type", contentType);
            var result = client.BeginRequest<T>(request);
            return result;
        }

        protected IAsyncResult BeginWithHammockNoResponse(RestClient client, WebMethod method, string path, params object[] segments)
        {
            path = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(path);
            request.Method = method;
            var result = client.BeginRequest(request);
            var response = client.EndRequest(result);
            SetResponse(response);

            return result;
        }

        protected IAsyncResult BeginWithHammock<T>(RestClient client, WebMethod method, string path, IDictionary<string, Stream> files, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;
            foreach (var file in files)
            {
                request.AddFile("media", file.Key, file.Value);
            }
            var result = client.BeginRequest<T>(request);
            return result;
        }

        protected IAsyncResult BeginWithHammock<T>(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;
            request.AddFile("media", media.FileName, media.Content);
            var result = client.BeginRequest<T>(request);
            return result;
        }

        protected IAsyncResult BeginWithHammockNoResponse(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;
            request.AddFile("media", media.FileName, media.Content);
            return client.BeginRequest(request);
        }

        protected void EndWithHammockNoResponse(RestClient client, IAsyncResult result)
        {
            var response = client.EndRequest(result);
            SetResponse(response);
            if (response.InnerException != null)
                throw response.InnerException;
        }

        protected T EndWithHammock<T>(RestClient client, IAsyncResult result)
        {
            var response = client.EndRequest<T>(result);
            SetResponse(response);
            return response.ContentEntity;
        }

        protected T EndWithHammock<T>(RestClient client, IAsyncResult result, TimeSpan timeout)
        {
            var response = client.EndRequest<T>(result, timeout);
            return response.ContentEntity;
        }

        protected void EndWithHammockNoResponse(RestClient client, IAsyncResult result, TimeSpan timeout)
        {
            var response = client.EndRequest(result, timeout);
            SetResponse(response);
            if (response.InnerException != null)
                throw response.InnerException;
        }

#endif

#if !SILVERLIGHT && !WINRT
        protected T WithHammock<T>(RestClient client, string path)
        {
            var request = PrepareHammockQuery(path);

            return WithHammockImpl<T>(client, request);
        }

        protected T WithHammock<T>(RestClient client, string path, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            return WithHammock<T>(client, url);
        }

        protected T WithHammock<T>(RestClient client, WebMethod method, string path)
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;

            return WithHammockImpl<T>(client, request);
        }

        protected T WithHammock<T>(RestClient client, WebMethod method, string path, byte[] bodyContent, string contentType)
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;
            request.AddPostContent(bodyContent);
            request.AddHeader("content-type", contentType);

            return WithHammockImpl<T>(client, request);
        }

        protected T WithHammock<T>(RestClient client, WebMethod method, string path, IDictionary<string, Stream> files, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;
            foreach (var file in files)
            {
                request.AddFile("media", file.Key, file.Value);
            }
            return WithHammockImpl<T>(client, request);
        }

        protected void WithHammockNoResponse(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;

            request.AddFile("media", media.FileName, media.Content);
            WithHammockNoResponseImpl(client, request);
        }

        protected void WithHammockNoResponse(RestClient client, WebMethod method, string path)
        {
            var request = PrepareHammockQuery(path);
            request.Method = method;

            WithHammockNoResponseImpl(client, request);
        }

        protected T WithHammock<T>(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());
            var request = PrepareHammockQuery(url);
            request.Method = method;
            request.QueryHandling = QueryHandling.AppendToParameters;

            request.AddFile("media", media.FileName, media.Content);
            return WithHammockImpl<T>(client, request);
        }

        protected T WithHammock<T>(RestClient client, WebMethod method, string path, params object[] segments)
        {
            var url = ResolveUrlSegments(path, segments.ToList());

            return WithHammock<T>(client, method, url);
        }

        protected void WithHammockNoResponse(RestClient client, WebMethod method, string path, params object[] segments)
        {
            BeginWithHammockNoResponse(client, method, path, segments);
        }

        //protected T WithHammockImpl<T>(RestClient client, RestRequest request)
        //{
        //    return WithHammockImpl<T>(client, request);
        //}

        protected void WithHammockNoResponseImpl(RestClient client, RestRequest request)
        {
            var response = client.Request(request);

            SetResponse(response);
        }

        protected T WithHammockImpl<T>(RestClient client, RestRequest request)
        {
            var response = client.Request<T>(request);

            SetResponse(response);

            var entity = response.ContentEntity;
            return entity;
        }
#endif

#if PLATFORM_SUPPORTS_ASYNC_AWAIT
		protected Task<TwitterAsyncResult<T1>> WithHammockTask<T1>(RestClient client, string path, params object[] segments) where T1 : class
		{
			var tcs = new TaskCompletionSource<TwitterAsyncResult<T1>>();
			try
			{
				WithHammock(client,
					(Action<T1, TwitterResponse>)((v, r) =>
					{
						try
						{
							tcs.SetResult(new TwitterAsyncResult<T1>(v, r));
						}
						catch (Exception ex)
						{
							tcs.SetException(ex);
						}
					}),
					path,
					segments
				);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}

			return tcs.Task;
		}

		protected Task<TwitterAsyncResult<T1>> WithHammockTask<T1>(RestClient client, WebMethod method, string path, byte[] bodyContent, string contentType) where T1 : class
		{
			var tcs = new TaskCompletionSource<TwitterAsyncResult<T1>>();
			try
			{
				WithHammock(client,
					method,
					(Action<T1, TwitterResponse>)((v, r) =>
					{
						try
						{
							tcs.SetResult(new TwitterAsyncResult<T1>(v, r));
						}
						catch (Exception ex)
						{
							tcs.SetException(ex);
						}
					}),
					path,
					bodyContent,
					contentType
				);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}

			return tcs.Task;
		}

		protected Task<TwitterAsyncResult<T1>> WithHammockTask<T1>(RestClient client, WebMethod method, string path, params object[] segments) where T1 : class
		{
			var tcs = new TaskCompletionSource<TwitterAsyncResult<T1>>();
			try
			{
			WithHammock(client, method,
				(Action<T1, TwitterResponse>)((v, r) =>
				{
					try
					{
						tcs.SetResult(new TwitterAsyncResult<T1>(v, r));
					}
					catch (Exception ex)
					{
						tcs.SetException(ex);
					}
				}),
				path,
				segments
			);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}

			return tcs.Task;
		}

		protected Task WithHammockNoResponseTask(RestClient client, WebMethod method, string path, params object[] segments) 
		{
			var tcs = new TaskCompletionSource<object>();
			try
			{
				WithHammockNoResponse(client, method,
					(Action<TwitterResponse>)((r) =>
					{
						try
						{
							if (r.InnerException == null)
								tcs.SetResult(r);
							else
								tcs.SetException(r.InnerException);
						}
						catch (Exception ex)
						{
							tcs.SetException(ex);
						}
					}),
					path,
					segments
				);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}

			return tcs.Task;
		}

		protected Task<TwitterAsyncResult<T1>> WithHammockTask<T1>(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments) where T1 : class
				{
					var tcs = new TaskCompletionSource<TwitterAsyncResult<T1>>();
					try
					{
						WithHammock<T1>(client, method,  
							(Action<T1, TwitterResponse>)((v, r) =>
							{
								try
								{
									tcs.SetResult(new TwitterAsyncResult<T1>(v, r));
								}
								catch (Exception ex)
								{
									tcs.SetException(ex);
								}
							}),
							media,
							ResolveUrlSegments(path, segments.ToList())
						);
					}
					catch (Exception ex)
					{
						tcs.SetException(ex);
					}

					return tcs.Task;
				}

		protected Task WithHammockNoResponseTask(RestClient client, WebMethod method, string path, MediaFile media, params object[] segments) 
		{
			var tcs = new TaskCompletionSource<object>();
			try
			{
				WithHammockNoResponse(client, method,
					(Action<TwitterResponse>)((r) =>
					{
						try
						{
							if (r.InnerException == null)
								tcs.SetResult(r);
							else
								tcs.SetException(r.InnerException);
						}
						catch (Exception ex)
						{
							tcs.SetException(ex);
						}
					}),
					media,
					ResolveUrlSegments(path, segments.ToList())
				);
			}
			catch (Exception ex)
			{
				tcs.SetException(ex);
			}

			return tcs.Task;
		}
#endif

        protected static T TryAsyncResponse<T>(Func<T> action, out Exception exception)
        {
            exception = null;
            var entity = default(T);
            try
            {
                entity = action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            return entity;
        }
    }
}
