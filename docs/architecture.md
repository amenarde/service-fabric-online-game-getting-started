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


[architecture]: ../docs/media/architecture.png
[route]: ../docs/media/routes.png
