# Adding a new feature to the game

In this document we are going to walk through how to implement a new feature in the sample game. The goal is to get a sense of how a new feature is implemented through the stack. This will mean touching the client javascript code, the stateless web service, and the necessary stateful service.

We are going to add a chat feature to the game. This will be a box that displays the latest messages sent in the chat, who sent them, and a timestamp. Similarly each player will be able to post to the chat. This will be an important feature in making the game more enjoyable and increasing the baseline playability of the game.

## Design

We must decide how to implement this feature in both the front and backend. Our implementation in the services will likely affect our client experience moreso than the other away around, so we will start there. 

### Backend

We assume this process will include pushing messages when the user submits them and fetching messages as other people write them. This means that the `Get Game` procedure we already have is probably a good option for gathering the chat data.

If we check the code or architecture file, we see that `Get Game`, in `RoomStoreController`, touches the general room dictionary to check that the room exists and then iterates over the entirety of the the active room dictionary. This gives us three options without drastically increasing the load of this function:

1. Distribute the chat data under the players in the active room dictionary.
  - This would require reconstructing the chat on each iteration, and chat information may be removed upon player logout.
2. Have the chat data be its own key in the active dictionary.
  - This would require an abuse of the dictionary to store two different types of data, and might make maintainability more difficult.
3. Store the chat data under the `roomid` in the general room dictionary.
  - This seems like a good option on all fronts, as we will not need to turn a read to a write on `Get Game` and this is a logical place to store the chat information.
  
For these reasons, we will proceed with option *3*. Next we consider the process of updating the chat. If we check the code we see that `Update Game` reads the general room dict, but does not upgrade the lock to a write lock. If we were to modify this code we would have to take an Update lock and then decide whether or not to use it. This may slow down a function we expect to get a lot of load. For these reasons we will make a new function, 'Update Room Chat', which will not have to touch the active room dictionary and will write to the general room dictionary.

### Front end

This is more straightforward. We would like to display a text box with recent chat data, and have an entry form to take in chat data. On implementation we will consider how color is updated as a model for how to implement this.

### Scope

There are some reasonable limitations to this feature that come to mind, and the exact limitations will depend a lot on considerations such as:
- What will this chat be used for?
- Do people expect this chat to maintain a long term chat history?
- Will people want to communicate long messages? Multimedia? Links?

These are all questions that would change how things are done and implementation. For our purposes, we are going to set restrictions that make an MVP of this feature easiest to implement. We are going to restrict message size to 100 bytes and only store and display the last 10 messages in a room. Similarly, when a room is closed the data will be lost, and a players chat will persist even if they leave the room.

## Implementation 

If multiple developers are working on this feature, the first step would be to set the API so that the front-end and backend developers can build to a contract. If built by a single developer, it is choice that drives whether to implement front -> back or back -> front. In this example we are going to work back -> front.

`RoomStoreController`
