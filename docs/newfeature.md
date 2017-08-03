# Adding a new feature to the game

In this document we are going to walk through how to implement a new feature in the sample game. The goal is to get a sense of how a new feature is implemented through the stack. This will mean touching the client javascript code, the stateless web service, and the necessary stateful service.

For this feature, we are going to add a chat feature to the game. This will be a box that displays the last messages sent in the chat, who sent them, and a timestamp. Similarly each player will be able to post to the chat.

## Design

We must decide how to implement this feature in both the front and back end. OurOur 
