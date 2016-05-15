/**
 * Float
 * 
 * A simplistic C# ASP.NET route handler with dynamic URLs and middleware capabilities.
 * 
 * @author
 * Stian Hanger (pdnagilum@gmail.com)
 * 
 * @source
 * https://github.com/nagilum/float
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Script.Serialization;

namespace FloatEngine {
	/// <summary>
	/// Main handler for the Float API route engine.
	/// </summary>
	public class RouteHandler {
		/// <summary>
		/// A list of global middleware functions.
		/// </summary>
		private static readonly List<Action<RouteWrapper>> globalMiddlewareFunctions = new List<Action<RouteWrapper>>(); 

		/// <summary>
		/// A list of headers to always respond with.
		/// </summary>
		private static readonly NameValueCollection globalResponseHeaders = new NameValueCollection();

		/// <summary>
		/// JSON serialize engine.
		/// </summary>
		private static readonly JavaScriptSerializer jss = new JavaScriptSerializer();

		/// <summary>
		/// All registered routes.
		/// </summary>
		private static readonly List<Route> routeTable = new List<Route>();

		/// <summary>
		/// Allowed HTTP methods for the routes.
		/// </summary>
		public enum HttpMethod {
			CONNECT,
			DELETE,
			HEAD,
			GET,
			OPTIONS,
			PATCH,
			POST,
			PUT,
			TRACE
		}

		/// <summary>
		/// Add a header that will be added to all responses.
		/// </summary>
		/// <param name="key">Key</param>
		/// <param name="value">Value</param>
		public static void AddGlobalHeader(string key, string value) {
			globalResponseHeaders.Add(key, value);
		}

		/// <summary>
		/// Add a middlewere function to the global list.
		/// </summary>
		/// <param name="function">The function to add.</param>
		public static void AddGlobalMiddleware(Action<RouteWrapper> function) {
			globalMiddlewareFunctions.Add(function);
		}

		/// <summary>
		/// Handle each request to the API.
		/// </summary>
		/// <param name="request">The active context request.</param>
		/// <param name="response">The active context response.</param>
		public static void HandleRequest(HttpRequest request, HttpResponse response) {
			response.Clear();

			HttpMethod httpMethod;

			if (!Enum.TryParse(request.HttpMethod, out httpMethod)) {
				response.StatusCode = 405;
				response.End();
			}

			// Add global headers.
			foreach (string key in globalResponseHeaders)
				response.Headers.Add(key, globalResponseHeaders[key]);

			// Check for auto-origins.
			if (Options.AutoAddAllowOrigin)
				response.Headers.Add("Access-Control-Allow-Origin", Options.AccessControlAllowOrigin);

			// Check for auto-reply for OPTIONS call.
			if (Options.HandleApiCallForHttpMethodOptions &&
			    httpMethod == HttpMethod.OPTIONS) {
				if (!Options.AutoAddAllowOrigin)
					response.Headers.Add("Access-Control-Allow-Origin", Options.AccessControlAllowOrigin);

				response.Headers.Add("Access-Control-Max-Age", string.Format("{0}", Options.AccessControlMaxAge));
				response.Headers.Add("Access-Control-Allow-Headers", string.Join(", ", Options.AccessControlAllowHeaders));
				response.Headers.Add("Access-Control-Allow-Methods", string.Join(", ", Options.AccessControlAllowMethods));
				response.End();
			}

			// Ready the wrapper object.
			var wrapper = new RouteWrapper {
				HttpMethod = httpMethod,
				RequestHeaders = request.Headers,
				RequestObject = request,
				RequestUrl = request.Url.AbsolutePath,
				RequestUrlSections = request.Url.AbsolutePath.Substring(1).Split('/'),
				ResponseHeaders = new NameValueCollection(),
				ResponseObject = response,
				RouteParams = new Dictionary<string, string>()
			};

			// Read the posted body params from the input-stream.
			try {
				using (var inputStream = request.InputStream) {
					using (var streamReader = new StreamReader(inputStream, Encoding.UTF8)) {
						wrapper.BodyParams = jss.Deserialize<Dictionary<string, string>>(streamReader.ReadToEnd());
					}
				}
			}
			catch {
				wrapper.BodyParams = new Dictionary<string, string>();
			}

			// Run all the global middleware functions.
			if (globalMiddlewareFunctions.Any()) {
				foreach (var gmwf in globalMiddlewareFunctions) {
					try {
						gmwf(wrapper);
					}
					catch (FloatException ex) {
						response.StatusCode = ex.HttpStatusCode ?? 500;
						if (ex.Output != null) writeOutput(response, ex.Output);
						response.End();
					}
					catch {
						response.StatusCode = 500;
						response.End();
					}
				}
			}

			// Find a matching route.
			var routes = routeTable
				.Where(r => r.RouteUrlSections.Length == wrapper.RequestUrlSections.Length &&
				            r.HttpMethod == wrapper.HttpMethod)
				.ToList();

			// Check each route for a match.
			foreach (var route in routes) {
				var hits = 0;

				for (var i = 0; i < wrapper.RequestUrlSections.Length; i++) {
					if (route.RouteUrlSections[i].StartsWith("{") &&
						route.RouteUrlSections[i].EndsWith("}")) {
						var key = route.RouteUrlSections[i].Substring(1, route.RouteUrlSections[i].Length - 2);
						var value = wrapper.RequestUrlSections[i];
						var valid = true;

						if (route.ValueValidators != null) {
							var regExValidator = route.ValueValidators[key];

							if (!string.IsNullOrWhiteSpace(regExValidator)) {
								var regex = new Regex(regExValidator);
								valid = regex.IsMatch(value);
							}
						}

						if (!valid)
							break;

						wrapper.RouteParams.Add(
							key,
							value);

						hits++;
					}
					else if (route.RouteUrlSections[i] == wrapper.RequestUrlSections[i]) {
						hits++;
					}
				}

				if (hits != wrapper.RequestUrlSections.Length)
					continue;

				wrapper.RouteUrl = route.RouteUrl;

				// If the accepted route has any middleware, execute it.
				if (route.MiddlewareFunctions != null &&
				    route.MiddlewareFunctions.Any()) {
					foreach (var mwf in route.MiddlewareFunctions) {
						try {
							mwf(wrapper);
						}
						catch (FloatException ex) {
							response.StatusCode = ex.HttpStatusCode ?? 500;
							if (ex.Output != null) writeOutput(response, ex.Output);
							response.End();
						}
						catch {
							response.StatusCode = 500;
							response.End();
						}
					}
				}

				// Call the main API method and handle the response/exception.
				object output = null;

				response.StatusCode = 200;

				try {
					output = route.RouteHandlerFunction(wrapper);
				}
				catch (FloatException ex) {
					response.StatusCode = ex.HttpStatusCode ?? 500;
					if (ex.Output != null) writeOutput(response, ex.Output);
					response.End();
				}
				catch {
					response.StatusCode = 500;
					response.End();
				}

				if (wrapper.ResponseHeaders.Count > 0)
					foreach (var key in wrapper.ResponseHeaders.AllKeys)
						response.Headers[key] = wrapper.ResponseHeaders[key];

				if (wrapper.ResponseStatusCode.HasValue)
					response.StatusCode = wrapper.ResponseStatusCode.Value;

				if (output != null)
					writeOutput(response, output);

				response.End();
			}
		}

		/// <summary>
		/// Add a new route to the route table.
		/// </summary>
		/// <param name="routeUrl">Descriptive route with variables.</param>
		/// <param name="httpMethod">HTTP method for route.</param>
		/// <param name="routeHandler">The function to execute when this route is called.</param>
		/// <param name="routeValues">A list of Regex validators for each variable in the route. (Optional)</param>
		/// <param name="middlewareFunctions">A list of middleware function to call before the main call. (Optional)</param>
		public static void RegisterRoute(
				string routeUrl,
				HttpMethod httpMethod,
				Func<RouteWrapper, object> routeHandler,
				Dictionary<string, string> routeValues = null,
				List<Action<RouteWrapper>> middlewareFunctions = null) {
			// Verify that the same route with the same HTTP method isn't already in the table.
			if (routeTable.SingleOrDefault(r => r.RouteUrl == routeUrl &&
			                                    r.HttpMethod == httpMethod) != null)
				throw new Exception("The same route with the same method already exist.");

			// Add route to table.
			routeTable.Add(
				new Route {
					RouteUrl = routeUrl,
					RouteUrlSections = routeUrl.Split('/'),
					HttpMethod = httpMethod,
					RouteHandlerFunction = routeHandler,
					ValueValidators = routeValues,
					MiddlewareFunctions = middlewareFunctions
				});
		}

		/// <summary>
		/// Add a new route to the route table.
		/// </summary>
		/// <param name="routeUrl">Descriptive route with variables.</param>
		/// <param name="httpMethods">A list of HTTP methods for route.</param>
		/// <param name="routeHandler">The function to execute when this route is called.</param>
		/// <param name="routeValues">A list of Regex validators for each variable in the route. (Optional)</param>
		/// <param name="middlewareFunctions">A list of middleware function to call before the main call. (Optional)</param>
		public static void RegisterRoute(
				string routeUrl,
				List<HttpMethod> httpMethods,
				Func<RouteWrapper, object> routeHandler,
				Dictionary<string, string> routeValues = null,
				List<Action<RouteWrapper>> middlewareFunctions = null) {
			foreach (var httpMethod in httpMethods) {
				// Verify that the same route with the same HTTP method isn't already in the table.
				if (routeTable.SingleOrDefault(r => r.RouteUrl == routeUrl &&
													r.HttpMethod == httpMethod) != null)
					throw new Exception("The same route with the same method already exist.");

				// Add route to table.
				routeTable.Add(
					new Route {
						RouteUrl = routeUrl,
						RouteUrlSections = routeUrl.Split('/'),
						HttpMethod = httpMethod,
						RouteHandlerFunction = routeHandler,
						ValueValidators = routeValues,
						MiddlewareFunctions = middlewareFunctions
					});
			}
		}

		/// <summary>
		/// Write given output to response stream.
		/// </summary>
		/// <param name="response">Response object to use.</param>
		/// <param name="output">Output to write.</param>
		private static void writeOutput(HttpResponse response, object output) {
			response.ContentType = Options.DefaultContentType;

			if (Options.SerializeToJSON)
				response.Write(jss.Serialize(output));
			else if (output is string)
				response.Write(output);
		}

		/// <summary>
		/// RouteTable list base class.
		/// </summary>
		private class Route {
			/// <summary>
			/// Descriptive route with variables.
			/// </summary>
			public string RouteUrl { get; set; }

			/// <summary>
			/// Section list of route.
			/// </summary>
			public string[] RouteUrlSections { get; set; }

			/// <summary>
			/// HTTP method for route.
			/// </summary>
			public HttpMethod HttpMethod { get; set; }

			/// <summary>
			/// The function to execute when this route is called.
			/// </summary>
			public Func<RouteWrapper, object> RouteHandlerFunction { get; set; }

			/// <summary>
			/// A list of Regex validators for each variable in the route.
			/// </summary>
			public Dictionary<string, string> ValueValidators { get; set; }

			/// <summary>
			/// A list of middleware function to call before the main call.
			/// </summary>
			public List<Action<RouteWrapper>> MiddlewareFunctions { get; set; } 
		}
	}

	/// <summary>
	/// Various options for the route engine.
	/// </summary>
	public class Options {
		/// <summary>
		/// Whether or not to serialize all output to JSON before forwarding it to the client.
		/// </summary>
		public static bool SerializeToJSON = true;

		/// <summary>
		/// The default content-type header to respond to the client with.
		/// </summary>
		public static string DefaultContentType = "application/json; charset=utf-8";

		/// <summary>
		/// Whether or not to automatically answer the OPTIONS call to the framework.
		/// </summary>
		public static bool HandleApiCallForHttpMethodOptions = true;

		/// <summary>
		/// A list of headers to respond with in an OPTIONS call.
		/// </summary>
		public static List<RouteHandler.HttpMethod> AccessControlAllowMethods = new List<RouteHandler.HttpMethod> { RouteHandler.HttpMethod.GET, RouteHandler.HttpMethod.POST, RouteHandler.HttpMethod.DELETE, RouteHandler.HttpMethod.OPTIONS };

		/// <summary>
		/// The allowed origin for calls towards this API.
		/// </summary>
		public static string AccessControlAllowOrigin = "*";

		/// <summary>
		/// Max age for API options call.
		/// </summary>
		public static int AccessControlMaxAge = 3600;

		/// <summary>
		/// Headers to reply as allowed.
		/// </summary>
		public static List<string> AccessControlAllowHeaders = new List<string> { "Content-Type", "Authorization" };

		/// <summary>
		/// Whether or not to automatically add the Access-Control-Allow-Origin header to all responses.
		/// </summary>
		public static bool AutoAddAllowOrigin = true;
	}

	/// <summary>
	/// HTTP status code bound exception, for terminating a function with a given status code.
	/// </summary>
	public class FloatException : Exception {
		/// <summary>
		/// The set HTTP status code.
		/// </summary>
		public int? HttpStatusCode { get; set; }

		/// <summary>
		/// Optional object to output.
		/// </summary>
		public object Output { get; set; }

		/// <summary>
		/// Init a new instance of the FloatException with a status code.
		/// </summary>
		/// <param name="httpStatusCode">HTTP status code to response with.</param>
		/// <param name="output">Optional object to output.</param>
		public FloatException(int httpStatusCode, object output = null) {
			this.HttpStatusCode = httpStatusCode;
			this.Output = output;
		}
	}

	/// <summary>
	/// Container for all things during the request life.
	/// </summary>
	public class RouteWrapper {
		/// <summary>
		/// The dynamic URL that matched the request URL.
		/// </summary>
		public string RouteUrl { get; set; }

		/// <summary>
		/// The actual request URL.
		/// </summary>
		public string RequestUrl { get; set; }

		/// <summary>
		/// Request URL divided into sections.
		/// </summary>
		public string[] RequestUrlSections { get; set; } 

		/// <summary>
		/// The HTTP method for the request.
		/// </summary>
		public RouteHandler.HttpMethod HttpMethod { get; set; }

		/// <summary>
		/// A list of variables from the route and their value.
		/// </summary>
		public Dictionary<string, string> RouteParams { get; set; }

		/// <summary>
		/// A list of posted variables and their value.
		/// </summary>
		public Dictionary<string, string> BodyParams { get; set; }

		/// <summary>
		/// A list of all headers from the request.
		/// </summary>
		public NameValueCollection RequestHeaders { get; set; }

		/// <summary>
		/// A list of headers to be added to the response.
		/// </summary>
		public NameValueCollection ResponseHeaders { get; set; }

		/// <summary>
		/// If you wish to set a specific HTTP status code for the response.
		/// </summary>
		public int? ResponseStatusCode { get; set; }

		/// <summary>
		/// The actual ASPx request object.
		/// </summary>
		public HttpRequest RequestObject { get; set; }

		/// <summary>
		/// The actual ASPx response object.
		/// </summary>
		public HttpResponse ResponseObject { get; set; }
	}
}