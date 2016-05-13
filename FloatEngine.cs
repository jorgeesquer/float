/**
 * @file
 * Float API Route Engine
 * 
 * @author
 * Stian Hanger (pdnagilum@gmail.com)
 * 
 * A simplistic C# ASP.NET API route handler with route variables and middleware capabilities.
 * 
 * How to use:
 * 
 * In your global.asax file, add the following in your Application_BeginRequest() function:
 * 
 *   FloatEngine.RouteHandler.HandleRoute(Request, Response);
 *   
 * That's all that is required to handle the actual calls. To add routes, just use
 * the following calls in your Application_Start() function:
 * 
 *   FloatEngine.RouteHandler.RegisterRoute(
 *     "api/user",
 *     FloatEngine.RouteHandler.HttpMethod.GET,
 *     MyClass.MyGetAllUsersFunction);
 * 
 * If you wish to add route variables with validation, as well as middleware, use the following:
 * 
 *   FloatEngine.RouteHandler.RegisterRoute(
 *     "api/user/{id}",
 *     FloatEngine.RouteHandler.HttpMethod.GET,
 *     MyClass.MyGetUserFunction,
 *     new Dictionary<string, string> {{"id", @"\d"}},
 *     new List<Action<HttpRequest>> { MyClass.MyMiddleware });
 *     
 * As you can see, you can add as many middleware functions as you want.
 * 
 * The route functions needs to accept the following parameters:
 * 
 *   // A list of all variables in the route itself.
 *   Dictionary<string, string> routeParams
 *   
 *   // A list of all posted body variables and values.
 *   Dictionary<string, string> bodyParams
 *   
 *   // The active context request object.
 *   HttpRequest request)
 *   
 */

using System;
using System.Collections.Generic;
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
			GET,
			POST,
			PUT,
			DELETE
		}

		/// <summary>
		/// Handle each request to the API.
		/// </summary>
		/// <param name="request">The active context request.</param>
		/// <param name="response">The active context response.</param>
		public static void HandleRoute(HttpRequest request, HttpResponse response) {
			response.Clear();
			response.Headers.Add("Access-Control-Allow-Origin", Options.AccessControlAllowOrigin);

			// Process the OPTIONS call and return API options.
			if (request.HttpMethod == "OPTIONS") {
				response.Headers.Add("Access-Control-Max-Age", string.Format("{0}", Options.AccessControlMaxAge));
				response.Headers.Add("Access-Control-Allow-Headers", string.Join(", ", Options.AccessControlAllowHeaders));
				response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
				response.End();
			}

			var routeSections = request.Url.AbsolutePath.Substring(1).Split('/');
			var routeParams = new Dictionary<string, string>();
			var routes = routeTable
				.Where(r => r.RouteUrlSections.Length == routeSections.Length &&
							r.HttpMethod.ToString() == request.HttpMethod);

			// Check each route for a match.
			foreach (var route in routes) {
				var hits = 0;

				for (var i = 0; i < routeSections.Length; i++) {
					if (route.RouteUrlSections[i].StartsWith("{") &&
						route.RouteUrlSections[i].EndsWith("}")) {
						var key = route.RouteUrlSections[i].Substring(1, route.RouteUrlSections[i].Length - 2);
						var value = routeSections[i];
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

						routeParams.Add(
							key,
							value);

						hits++;
					}
					else if (route.RouteUrlSections[i] == routeSections[i]) {
						hits++;
					}
				}

				if (hits != routeSections.Length)
					continue;

				// If the accepted route has any middleware, execute it.
				if (route.Middleware != null &&
				    route.Middleware.Any()) {
					foreach (var action in route.Middleware) {
						try {
							action(request);
						}
						catch (FloatException ex) {
							response.StatusCode = ex.HttpStatusCode ?? 500;
							response.End();
						}
						catch {
							response.StatusCode = 500;
							response.End();
						}
					}
				}

				// Read the posted body params from the input-stream.
				Dictionary<string, string> bodyParams;

				try {
					using (var inputStream = request.InputStream) {
						using (var streamReader = new StreamReader(inputStream, Encoding.UTF8)) {
							bodyParams = jss.Deserialize<Dictionary<string, string>>(streamReader.ReadToEnd());
						}
					}
				}
				catch {
					bodyParams = new Dictionary<string, string>();
				}

				// Call the main API method and handle the response/exception.
				object output = null;

				response.StatusCode = 200;

				try {
					output = route.RouteHandler(
						routeParams,
						bodyParams,
						request);
				}
				catch (FloatException ex) {
					response.StatusCode = ex.HttpStatusCode ?? 500;
					response.End();
				}
				catch {
					response.StatusCode = 500;
					response.End();
				}

				if (output != null) {
					response.ContentType = "application/json; charset=utf-8";
					response.Write(jss.Serialize(output));
				}

				response.End();

				break;
			}
		}

		/// <summary>
		/// Add a new route to the route table.
		/// </summary>
		/// <param name="routeUrl">Descriptive route with variables.</param>
		/// <param name="httpMethod">HTTP method for route.</param>
		/// <param name="routeHandler">The function to execute when this route is called.</param>
		/// <param name="routeValues">A list of Regex validators for each variable in the route. (Optional)</param>
		/// <param name="middleware">A list of middleware function to call before the main call. (Optional)</param>
		public static void RegisterRoute(
				string routeUrl,
				HttpMethod httpMethod,
				Func<Dictionary<string, string>, Dictionary<string, string>, HttpRequest, object> routeHandler,
				Dictionary<string, string> routeValues = null,
				List<Action<HttpRequest>> middleware = null) {
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
					RouteHandler = routeHandler,
					ValueValidators = routeValues,
					Middleware = middleware
				});
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
			public Func<Dictionary<string, string>, Dictionary<string, string>, HttpRequest, object> RouteHandler { get; set; }

			/// <summary>
			/// A list of Regex validators for each variable in the route.
			/// </summary>
			public Dictionary<string, string> ValueValidators { get; set; }

			/// <summary>
			/// A list of middleware function to call before the main call.
			/// </summary>
			public List<Action<HttpRequest>> Middleware { get; set; } 
		}
	}

	/// <summary>
	/// Various options for the API engine.
	/// </summary>
	public class Options {
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
		/// Init a new instance of the FloatException with a status code.
		/// </summary>
		/// <param name="httpStatusCode">HTTP status code to response with.</param>
		public FloatException(int httpStatusCode) {
			this.HttpStatusCode = httpStatusCode;
		}
	}
}