# Application design

The application consists of three service fabric services and the javascript client:

| Component | Role |
| :--- | :--- |
| Web Client | Handles all of the game state, such as drawing the game and the event loop. Interacts with user through key presses and forms, verifying entry and sending requests. |
| Web Service | Primarily routes requests to right place and enumerates over partitions to gather high-level information. |
| Player Manager | Stateful service that holds long term account data and player data when a player is logged out. |
| Room Manager | Holds the live game state in active rooms that make it easy for clients to get up-to-date game information and update the game state of their player. |

## Architecture
Here is a general look at how these different parts interact in the game:

![Architecture Diagram][architecture]

Here is a look at how different actions by the user get routed through the application:
![Route Diagram][route]

## Status codes to communicate state

Similarly, the API returns different status codes to communicate between services. Here are the status codes and their implied meaning in this application:

| Code | Meaning |
| --- | :--- |
| 200 | This code signifies success, if there is content it will contain the result of the call. |
| 400 | This code signifies a bad request, for example if the user tries to log in with an account that is already logged in. |
| 503 | This is a retry request, which means there was a temporary failure. If received by the WebService this means to re-resolve the request and try again, and if it is received by the client either the user or the client can handle retrying, depending on context. |
| 500 | This is a failure code, which will generally be propogated to the user asking them to either refresh the page, come back later, or submit a bug report. |

## Service Walkthrough: `PlayerManager/`

Here we are going to walk through a service and what different files do.

**`PlayerManager.cs`:** If you are not using a controller model, like this application does, here is where the most of your workload would be written. Here you can write functions that can be called by your controllers and by other services using ServiceProxy. `RunAsync` also lives here, as well as your `CreateServiceReplicaListeners`, which we use to create the http listener that leads to our controller. We can add new arguments to our controllers constructor by using `.AddSingleton()`.

**`Controllers/PlayerStoreController.cs`:** In this application, most of the workload is done by this controller. Since we gave it access to `StateManager` and other pertinent information in `PlayerManager.cs`, it can work with our Reliable Collections and route calls. Http requests are configured to come to this controller and automatically land on the necessary function. For example, `[Route("api/[controller]/NewGame")]` makes it such that requests starting with `[ReverseProxyAddress]/api/PlayerStore/NewGame/` get served by `NewGameAsync`.

**`Startup.cs`:** For most basic purposes, you will not need to touch this file. I have `services.AddMvc()`, which is used to set up my http pipeline. Other than that this file takes configurations you have and lets the environment know when the service first starts up.

**`ConfigSettings.cs`:** Here you can see there is `RoomManagerName`, `ReverseProxyPort`, and then another mention of them in `UpdateConfigSettings`. These are used to make this service aware of the name of the `RoomManager` service, and which port `ReverseProxy` is on.

**`PackageRoot/Config/Settings.xml`**: This is where those configuration settings are coming from, and these represent two parameters that build will require to be defined in the applications service manifest, so that this service knows about the other two.

You most likely will not need to touch any of the other files in the Service, which generally serve purposes for debugging, logging, and getting the service set up in its environment.

[architecture]: ../docs/media/architecture.png
[route]: ../docs/media/routes.png

## Game Design Features Walkthrough

This game is also meant to demonstrate some useful features for online games in general. These can be used as reference for implementations of similar features, or be built on top of for additional functionality.

| Feature | Description | Implementation |
| :------ | :---------- | :------------- |
| Input verification | Both check users input such as color codes, and help prevent malicious intent or cheating | `newgame()` in `site.js`; `NewGameAsync()` in `PlayerController.cs` |
| Account Metrics | Allows for producers to understand how people are playing the game and add in new metrics | Metrics functions in `site.js`; `PlayerController.cs`, `PlayerStoreController.cs` |
| Logging out inactive players | This helps ensure players can't find them in a state where they can't log in or are stuck logged in | `beforeunload` in `site.js`, `RunAsync()` in `RoomManager.cs` |
| Random start parameters | Used to give new users a random color and position, can be adapted for any starting parameters | `Scenario 1` in `NewGame()` in `PlayerStoreController.cs` |
| Multiple types of rooms | Allows for games to take place in different rooms. Could add functionality that moves players between rooms actively. | `drawGame()` in `Game.js`; as well as verified and stored with rooms |

## Next Steps

Check out these other helpful documents:
- [Learn important Service Fabric concepts to support your readthrough of the code][3]
- [Walk through the implementation of a new feature in the game.][5]

[3]: ../master/docs/concepts.md
[5]: ../master/docs/newfeature.md
