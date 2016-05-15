# Float

A simplistic C# ASP.NET route handler with dynamic URLs and middleware capabilities.

I created this framework mostly as a means to easily setup a RESTful API with minimal hassle, but it can easily be used for normal webpage route/rendering too. It was written as a replacement for the ASPx website default route handler, but might work with ASPx webapp's too, I'm not sure.

Topics:

* [How to Register Routes](#how-to-register-routes)
* [How to Handle Routes](#how-to-handle-routes)
* [Middleware](#middleware)
* [RouteWrapper](#routeWrapper)
* [Exception Handling](#exception-handling)
* [Options](#options)
* [How to Make a Response Other Than 200 OK](#how-to-make-a-response-other-than-200-ok)
* [How to Make a Response Other Than JSON](#how-to-make-a-response-other-than-json)
* [Settings in Web.config](#settings-in-web-config)


## <a name="how-to-register-routes"></A>How to Register Routes

You can add a route to the route table from anywhere in you app, but the cleanest way, I suppose, is to use the ```Application_Start``` function in the Global.asax file.

```c#
FloatEngine.RouteHandler.RegisterRoute(
	"api/user",
	FloatEngine.RouteHandler.HttpMethod.GET,
	MyClass.MyGetAllUsersFunction);
```

This will make a GET request to ```/api/user``` go to ```MyClass.MyGetAllUsersFunction``` to be handled. Anything returned from that function will, by default, be serialized to JSON and returned to the client.

Your ```MyClass.MyGetAllUsersFunction``` must be setup to accept a single argument, the [```FloatEngine.RouteWrapper```](#objects-to-use) type, and the function defaults to ```object``` as return type. The [```FloatEngine.RouteWrapper```](#objects-to-use) provides everything you need to know about the request, as well as means to manipulate the response.

A simple ```MyClass.MyGetAllUsersFunction``` might look like this:

```c#
public static object MyGetAllUsersFunction(FloatEngine.RouteWrapper wrapper) {
	return new [] {
		new {
			id = 1,
			name = "Awesome Person"
		},
		new {
			id = 2,
			name = "Even More Awesome Person"
		}
	};
}
```

This will yield the following JSON output:

```json
[
	{
		id: 1,
		name: "Awesome Person"
	},
	{
		id: 2,
		name: "Even More Awesome Person"
	}
]
```

---

You can add more arguments to the ```RegisterRoute``` function to flesh out your handling. This is the full format of the function arguments:

* ```string``` The route to parse.
* ```HttpMethod``` -or- ```List<HttpMethod>``` A single, or multiple, HTTP methods to accept. Setting this to ```null``` will add all HTTP methods.
* ```Func<RouteWrapper, object>``` The function to call when the route is requested.
* ```Dictionary<string, string>``` A dictionary list of variables from the route and their regex validator strings. (Optional)
* ```List<Action<RouteWrapper>>``` A list of middleware functions to execute, in order, before the main call. (Optional)

A full register will look something like this:

```c#
FloatEngine.RouteHandler.RegisterRoute(
	"api/user/{id}",
	new List<FloatEngine.RouteHandler.HttpMethod> {
		FloatEngine.RouteHandler.HttpMethod.GET,
		FloatEngine.RouteHandler.HttpMethod.POST,
		FloatEngine.RouteHandler.HttpMethod.DELETE
	},
	MyClass.MyUserHandleFunction,
	new Dictionary<string, string> {{"id", @"\d"}},
	new List<Action<RouteWrapper>> {
		MyClass.MyFirstMiddleware,
		MyClass.MySecondMiddleware
	});
```

* This call will add three routes:
	* GET to ```/api/user/{id}```
	* POST to ```/api/user/{id}```
	* DELETE to ```/api/user/{id}```
* The ```id``` part of the route will validate against the ```@"\d"``` regex.
* It will first execute the ```MyFirstMiddleware``` function, and if that's successful, the second ```MySecondMiddleware``` function, and if that's successfull, finally the ```MyUserHandleFunction``` function.


## <a name="how-to-handle-routes"></A>How to Handle Routes

To make the Float framework respond to your routes, you have to add the following to your ```Application_BeginRequest()``` function in Global.asax:

```c#
FloatEngine.RouteHandler.HandleRequest(Request, Response);
```

This will send all requests that the ASPx engine picks up and route them to the Float framework for handling.


## <a name="middleware"></a>Middleware

Middleware functions are functions that have access to the request object and the response object. It's meant for various handling before the main function, like authorization and the like. The functions are ```void``` as their not meant to return anything. They can however throw errors which will be handled by the framework then returned to the client.

There are two ways to add middleware functions, while registering a route or globally. To add it while registering a route, just use the middleware argument in the ```RegisterRoute``` function.

To add a global middleware function, use the following:

```c#
FloatEngine.RouteHandler.AddGlobalMiddleware(MyClass.MyGlobalMiddleware);
```

Beware, global middleware will be called before every route functions and their local middleware.


## <a name="headers"></a>Headers

There are two ways to add custom headers to the response, globally, or locally for each request.

To globally add a header, which will be included in every response.

```c#
FloatEngine.RouteHandler.AddGlobalHeader("key", "value");
```

The ```RouteWrapper``` contains a ```NameValueCollection``` called ```ResponseHeaders``` which can be manipulated at any time during the request life, both in any middleware and the main route function. These headers will be added to the response in the end.


## <a name="routewrapper"></a>RouteWrapper

The ```RouteWrapper``` object is the object passed to all the middleware function as well as the main route function. It contains the following properties:

* RouteUrl (```string```) - The dynamic URL that matched the request URL.
* RequestUrl (```string```) - The actual request URL.
* RequestUrlSections (```string[]```) - Request URL divided into sections.
* HttpMethod (```HttpMethod```) - The HTTP method for the request.
* RouteParams (```Dictionary<string, string>```) - A list of variables from the route and their value.
* BodyParams (```Dictionary<string, string>```) - A list of posted variables and their value.
* RequestHeaders (```NameValueCollection```) - A list of all headers from the request.
* ResponseHeaders (```NameValueCollection```) - A list of headers to be added to the response. This list can be manipulated throughout the life of the request.
* ResponseStatusCode (```int```) - If you wish to set a specific HTTP status code for the response.
* RequestObject (```HttpRequest```) - The actual ASPx request object. Just in case Float doesn't provide all necessary info and handlers for your needs.
* ResponseObject (```HttpResponse```) - The actual ASPx response object. Just in case Float doesn't provide all necessary info and handlers for your needs.


## <a name="exception-handling"></a>Exception Handling

If an unhandled exception is thrown anywhere in the Float framework it ends the entire request life and responds with a 500 Server Error. The point of this is so you can terminate the entire request life inside any of the middleware functions, or the main function without having to implement any logic for it.

To respond with anything else than 500 for an error, you can use the ```FloatException```. ```FloatException``` is just a normal Exception object with a few added properties, one of which is ```HttpStatusCode```, an int, which can be set in the constructor.

```c#
throw new FloatException(404);
```

This will throw a normal exception, but tell the framework to use a different HTTP status code instead of 500.

You can also respond with more than just the HTTP status code for errors, such as content. To do this, just use the second property added to the ```FloatException```, an object which default to ```null```.

```c#
throw new FloatException(
	404,
	new {
		message = "The user in my awesome system was not found."
	});
```

The response will be serialized to JSON and outputted. If you wish to respond with HTML, or something else, set that up with the [Options calls](#options).


## <a name="options"></a>Options

Float has several options which can be accessed through the ```FloatEngine.Options``` object.

* SerializeToJSON (```bool```) - Whether or not to serialize all output to JSON before forwarding it to the client. Defaults to ```true```.
* DefaultContentType (```string```) - The default content-type header to respond to the client with. Defaults to ```application/json; charset=utf-8```.
* HandleApiCallForHttpMethodOptions (```bool```) - Whether or not to automatically answer the OPTIONS call to the framework with headers: ```Access-Control-Max-Age```, ```Access-Control-Allow-Headers```, ```Access-Control-Allow-Methods```, and ```Access-Control-Allow-Origin```.
* AccessControlMaxAge (```int```) - The max age for the OPTIONS call. Defaults to 3600.
* AccessControlAllowHeaders (```List<string>```) - A list of headers to respond with in an OPTIONS call. Defaults to ```Content-Type``` and ```Authorization```.
* AccessControlAllowMethods (```List<HttpMethod>```) - A list of methods to allow for access control. Defaults to ```GET```, ```POST```, ```DELETE```, and ```OPTIONS```.
* AccessControlAllowOrigin (```string```) - The default origin setting to respond with. Defaults to ```*```.
* AutoAddAllowOrigin (```bool```) - Whether or not to automatically add the ```Access-Control-Allow-Origin``` header to all responses.


## <a name="how-to-make-a-response-other-than-200-ok"></a>How to Make a Response Other Than 200 OK

There are two ways to get a different HTTP status code than 200 when responding. You can use the [```FloatException```](#exception-handling), but that's for error handling. Maybe you wish to respond with another status code while also going through with the main route function. This is where the ```ResponseStatusCode``` property on the [```RouteWrapper```](#routewrapper) object comes in. This int defaults to 200, but if you set anything else during one of the middlewares or the main route function, this is what will be used finally for the output.


## <a name="how-to-make-a-response-other-than-json"></a>How to Make a Response Other Than JSON

When you return something in the main route function it will by default be serialized to JSON and returned to the client. If you wish to output in some other format, f.eks: HTML, you can override this by setting the ```FloatEngine.Options.SerializeToJSON``` to ```false```, and the ```FloatEnging.Options.DefaultContentType``` to ```text/html```. What you return in the main route function will now be served right to the client with ```text/html``` as the content-type.


## <a name="settings-in-web-config"></a>Settings in Web.config

There are two problems with the default settings in a ASPx website, error handling and HTTP methods.

Throwing an error will normally give the ASPx or IIS error page, depending on your local settings. You can override this in the Web.config file with the following settings.

```xml
<configuration>
	<system.web>
		<customErrors mode="Off"></customErrors>
	</system.web>
	<system.webServer>
		<httpErrors existingResponse="PassThrough"></httpErrors>
	</system.webServer>
</configuration>
```

This will turn off custom error and allow the error data provided by the Float framework to come through to the client.

---

By default the ASPx engine only allows GET and POST, which is a bit lacking if you're developing a RESTful API. To enable more HTTP methods, use the following settings.

```xml
<configuration>
	<system.webServer>
		<handlers>
			<remove name="ExtensionlessUrlHandler-ISAPI-4.0_32bit"/>
			<remove name="ExtensionlessUrlHandler-ISAPI-4.0_64bit"/>
			<remove name="ExtensionlessUrlHandler-Integrated-4.0"/>
			<add name="ExtensionlessUrlHandler-ISAPI-4.0_32bit" path="*." verb="GET,HEAD,POST,DEBUG,PUT,DELETE,PATCH,OPTIONS" modules="IsapiModule" scriptProcessor="%windir%\Microsoft.NET\Framework\v4.0.30319\aspnet_isapi.dll" preCondition="classicMode,runtimeVersionv4.0,bitness32" responseBufferLimit="0"/>
			<add name="ExtensionlessUrlHandler-ISAPI-4.0_64bit" path="*." verb="GET,HEAD,POST,DEBUG,PUT,DELETE,PATCH,OPTIONS" modules="IsapiModule" scriptProcessor="%windir%\Microsoft.NET\Framework64\v4.0.30319\aspnet_isapi.dll" preCondition="classicMode,runtimeVersionv4.0,bitness64" responseBufferLimit="0"/>
			<add name="ExtensionlessUrlHandler-Integrated-4.0" path="*." verb="GET,HEAD,POST,DEBUG,PUT,DELETE,PATCH,OPTIONS" type="System.Web.Handlers.TransferRequestHandler" preCondition="integratedMode,runtimeVersionv4.0"/>
		</handlers>
	</system.webServer>
</configuration>
```