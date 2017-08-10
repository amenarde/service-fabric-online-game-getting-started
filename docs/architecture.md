# Application architecture

The application consists of three service fabric services and the javascript client:

| Component | Role |
| :--- | :--- |
| Web Client | Handles all of the game state, such as drawing the game and the event loop. Interacts with user through key presses and forms, verifying entry and sending requests. |
| Web Service | Primarily routes requests to right place and enumerates over partitions to gather high-level information. |
| Player Manager | Stateful service that holds long term account data and player data when a player is logged out. |
| Room Manager | Holds the live game state in active rooms that make it easy for clients to get up-to-date game information and update the game state of their player. |

Here is a general look at how these different parts interact in the game:

![Architecture Diagram][architecture]

Here is a look at how different actions by the user get routed through the application:
![Route Diagram][route]

Similarly, the API returns different status codes to communicate between services. Here are the status codes and their implied meaning in this application:

| Code | Meaning |
| --- | :--- |
| 200 | This code signifies success, if there is content it will contain the result of the call. |
| 400 | This code signifies a bad request, for example if the user tries to log in with an account that is already logged in. |
| 503 | This is a retry request, which means there was a temporary failure. If received by the WebService this means to re-resolve the request and try again, and if it is received by the client either the user or the client can handle retrying, depending on context. |
| 500 | This is a failure code, which will generally be propogated to the user asking them to either refresh the page, come back later, or submit a bug report. |

[architecture]: ../docs/media/architecture.png
[route]: ../docs/media/routes.png
