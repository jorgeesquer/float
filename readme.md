﻿# Float

A simplistic C# ASP.NET API route handler with route variables and middleware capabilities.

## How to use

In your global.asax file, add the following in your Application_BeginRequest() function:

	FloatEngine.RouteHandler.HandleRoute(Request, Response);
 
That's all that is required to handle the actual calls. To add routes, just use the following calls in your Application_Start() function:

	FloatEngine.RouteHandler.RegisterRoute(
		"api/user",
		FloatEngine.RouteHandler.HttpMethod.GET,
		MyClass.MyGetAllUsersFunction);

If you wish to add route variables with validation, as well as middleware, use the following:

	FloatEngine.RouteHandler.RegisterRoute(
		"api/user/{id}",
		FloatEngine.RouteHandler.HttpMethod.GET,
		MyClass.MyGetUserFunction,
		new Dictionary<string, string> {{"id", @"\d"}},
		new List<Action<HttpRequest>> { MyClass.MyMiddleware });

As you can see, you can add as many middleware functions as you want.

The route functions needs to accept the following parameters:

	// A list of all variables in the route itself.
	Dictionary<string, string> routeParams
  
	// A list of all posted body variables and values.
	Dictionary<string, string> bodyParams
 
	// The active context request object.
	HttpRequest request)