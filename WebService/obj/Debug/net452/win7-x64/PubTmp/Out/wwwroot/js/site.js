// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Authored by Antonio Menarde.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------



/**
 * These times represent the frequency of client draws and requests, and should be chosen on the basis of a few factors:
 * CLIENT_REFRESH_TIME: how fast client's game state is redrawn // should be most often since does not send messages
 * SERVER_READ_TIME: how often client will try to get new state, should be second most often since we can expect the game state as a
 *      whole to update more frequently than a single players game state.
 * SERVER_PUSH_TIME: Does not need to be often because of client refreshing, but too long will lead to bad response for other clients.
 */
const CLIENT_REFRESH_TIME = 50; var drawgamerefresh; 
const SERVER_READ_TIME = 75;
const SERVER_PUSH_TIME = 100;

const ROOM_GET_TIME = 1000; var showroomrefresh;

const ROOM_OPTIONS = ["Office", "Garden", "Cafe"];

// These structures are used to manage the relevant game state for the client and effectively communicate with the controller.
var clientgamestate = {
    'playerid': null,
    'roomid': null,
    'xpos': 0,
    'ypos': 0,
    'color': null,
    'roomdata': null,
    'roomtype': null
};
var servergamestate = {
    'xpos': 0,
    'ypos': 0,
    'color': null
};



/**
 * On window load hide irrelevant divs to maintain order and start to retrieve rooms from backend.
 */
window.onload = function () {
    document.getElementsByClassName("gameDiv")[0].style.display = 'none';
    showrooms(true);
    showroomrefresh = setInterval(function () {
        showrooms(false);
    }, ROOM_GET_TIME);
};

/**
* On window close make an attempt to end the game.
*/
window.addEventListener('beforeunload', function (event) {
    endgame();
}, false);


/**
 * LOGIN SECTION
 * newgame
 * showrooms
 */

/**
 * Sends a request to the controller to establish a new game.
 * Initializes the game canvas and retrieves the first server state that sets the client state
 * @argument {boolean} bool true if chooosing an already existing room, false if a new room.
 * @argument {button} context so that the function can reach in and get the name of the room.
 * These arguments tell this function where to gather the relevant information from the html.
 */
function newgame(bool, context) {

    //Decides where to get room name
    var roomid; var roomtype;
    var playerid = document.getElementById("pidform").value;

    if (bool === true) {
        roomid = context.name.substring(0, context.name.indexOf(','));
        roomtype = context.name.substring(context.name.indexOf(',') + 1);
    }
    else {
        roomid = document.getElementById("newgamename").value;
        roomtype = document.getElementById("newgametype").value;
    }

    if (roomid === "") { document.getElementById("status").innerHTML = "Please enter a room name"; return; }
    if (playerid === "") { document.getElementById("status").innerHTML = "Please enter a username"; return; }
    if (roomtype === "") { document.getElementById("status").innerHTML = "Something went wrong with room typing"; return; }

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
                if (returnData) {
                    document.getElementById("status").innerHTML = "Successfully logged in";

                    clientgamestate.roomtype = returnData.value;

                    clearInterval(showroomrefresh);

                    clientgamestate.playerid = playerid;
                    clientgamestate.roomid = roomid;

                    //initialize canvas
                    gameArea.start();

                    //run getgame in the "new game" setting
                    getgame(true, true);

                    //hide login-related html
                    document.getElementsByClassName("gameDiv")[0].style.display = '';
                    document.getElementsByClassName("loginDiv")[0].style.display = 'none';
                }
                else { document.getElementById("status").innerHTML = "Something went wrong, please referesh webpage"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };

    http.open("GET", "api/Player/NewGame/?playerid=" + document.getElementById("pidform").value + "&roomid=" + roomid + "&roomtype=" + roomtype);
    http.send();
}



/**
 * Asks the controller for the current available rooms. This allows the user to see and choose all available rooms, or
 * to make their own. Refreshes periodically until a game has begun. Handles populating the related table.
 * @argument {boolean} firsttime Determines whether to draw the room input box or not
 */
function showrooms(firsttime) {
    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = http.responseText;
            if (http.status < 400) {
                if (returnData) {
                    returnData = JSON.parse(returnData);
                    var table = document.getElementById('roomstable');
                    var row;
                    var namecell; var name;
                    var typecell; var type;
                    var numcell; var num;
                    var buttoncell; var button;


                    if (firsttime) {
                        //Header
                        row = table.insertRow();
                        namecell = row.insertCell(); name = document.createTextNode("  Room Name  "); namecell.appendChild(name);
                        typecell = row.insertCell(); type = document.createTextNode("  Room Type  "); typecell.appendChild(type);
                        numcell = row.insertCell(); num = document.createTextNode("# Players"); numcell.appendChild(num);
                        buttoncell = row.insertCell(); button = document.createTextNode("Choose / Create"); buttoncell.appendChild(button);

                        //New Room Entry Form
                        row = table.insertRow();

                        namecell = row.insertCell();
                        name = document.createElement('INPUT');
                        name.setAttribute('type', 'text');
                        name.setAttribute('id', 'newgamename');
                        name.setAttribute('placeholder', 'Room Name');
                        namecell.appendChild(name);

                        typecell = row.insertCell();
                        type = document.createElement('select');
                        for (i = 0; i < ROOM_OPTIONS.length; i++) {
                            var option = document.createElement("option");
                            option.value = ROOM_OPTIONS[i];
                            option.text = ROOM_OPTIONS[i];
                            type.appendChild(option);
                        }
                        type.setAttribute('id', 'newgametype');
                        typecell.appendChild(type);

                        numcell = row.insertCell(); num = document.createTextNode(""); numcell.appendChild(num);

                        buttoncell = row.insertCell();
                        button = document.createElement("button");
                        button.setAttribute('type', 'button');
                        button.setAttribute('id', "newgamebutton");
                        button.setAttribute('onclick', 'newgame(false, this)');
                        buttoncell.appendChild(button);
                    }

                    //Delete all rows beside your first two
                    while (table.rows.length > 2) {
                        table.deleteRow(2);
                    }

                    //Populate table with new room data
                    for (i = 0; i < returnData.length; i++) {
                        row = table.insertRow();
                        namecell = row.insertCell(); name = document.createTextNode(returnData[i].Key); namecell.appendChild(name);
                        typecell = row.insertCell(); type = document.createTextNode(returnData[i].Value.roomtype); typecell.appendChild(type);
                        numcell = row.insertCell(); num = document.createTextNode(returnData[i].Value.numplayers); numcell.appendChild(num);

                        buttoncell = row.insertCell();
                        button = document.createElement("button");
                        button.setAttribute('type', 'button');
                        button.setAttribute('name', returnData[i].Key + "," + returnData[i].Value.roomtype );
                        button.setAttribute('onclick', 'newgame(true, this)');
                        buttoncell.appendChild(button);
                    }

                }
                else {
                    document.getElementById("status").innerHTML = "Something went wrong, please referesh webpage";
                }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };
    http.open("GET", "api/Room/GetRooms");
    http.send();
}

/**
 * UPDATE GAME SECTION
 * updategamewatcher
 * toUpdate
 * updategame
 * endgame
 */

/**
 * This function looks for key presses so that it knows to update the clients game view. This function is designed to allow for smooth
 * gameplay for the player. A different watcher will look for these changes and periodically push to the backend.
 */
function updategamewatcher() {

    if (gameArea.keys && gameArea.keys[37]) {
        if (clientgamestate.xpos <= -6){
            clientgamestate.xpos = -6;
        }
        else
        {
            clientgamestate.xpos -= 1;
        }
        
        
        //this passes in a copy of the game state, reference does not garauntee consistency for duration of draw function
        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    else if (gameArea.keys && gameArea.keys[39]) {
        if (clientgamestate.xpos >= 94) {
            clientgamestate.xpos = 94;
        }
        else {
            clientgamestate.xpos += 1;
        }

        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    if (gameArea.keys && gameArea.keys[38]) {
        if (clientgamestate.ypos <= -6) {
            clientgamestate.ypos = -6;
        }
        else {
            clientgamestate.ypos -= 1;
        }

        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    else if (gameArea.keys && gameArea.keys[40]) {
        if (clientgamestate.ypos >= 90) {
            clientgamestate.ypos = 90;
        }
        else {
            clientgamestate.ypos += 1;
        }

        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }

}

/**
 * Validates color changes when they are entered.
 */
function updateColor() {
    var colorchange = document.getElementById("colorupdateform").value;
    if (colorchange === "")
    {
        document.getElementById("status").innerHTML = "You must enter a HEX color code.";
        return;
    }

    if (!colorchange.startsWith('#'))
    {
        colorchange = '#'.concat(colorchange);
    }
    //blackbox regex that verifies color formatting
    var validcolor = /(^#[0-9A-F]{6}$)|(^#[0-9A-F]{3}$)/i.test(colorchange);
    if (!validcolor)
    {
        document.getElementById("status").innerHTML = "This is not a valid HEX color code. Try #E79380";
        document.getElementById("colorupdateform").value = "";
    }
    else
    {
        colorchange = colorchange.substring(1);
        clientgamestate.color = colorchange;
        document.getElementById("status").innerHTML = "Color update change queued";
        document.getElementById("colorupdateform").value = "";
    }
}

/**
 * Checks whether the client's state has deviated from the last known server state, which would prompt an update request.
 */
function toupdate() {
    if (clientgamestate.xpos !== servergamestate.xpos ||
        clientgamestate.ypos !== servergamestate.ypos ||
        clientgamestate.color !== servergamestate.color) {
        updategame();
    }
    else {
        window.setTimeout(function () { toupdate(); }, SERVER_PUSH_TIME);
    }
}

/**
 * If the update change listener finds that the client state has deviated from the server state, it will prompt a message to the
 * backend, sent here.
 */
function updategame() {

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            window.setTimeout(function () { toupdate(); }, SERVER_PUSH_TIME);
            returnData = http.responseText;
            if (http.status < 400) {
                if (returnData) {
                    document.getElementById("status").innerHTML = "Successfully updated game";
                }
                else { status.innerHTML = " updategame error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData;
            }
        }
    };

    //TODO should send the standard player JSON type, rather than individual fields
    http.open("GET", "api/Room/UpdateGame/?playerid=" + clientgamestate.playerid + "&roomid=" + clientgamestate.roomid +
        "&xpos=" + clientgamestate.xpos + "&ypos=" + clientgamestate.ypos + "&color=" + clientgamestate.color);
    http.send();
}

/**
 * Reaches in and gets the current game state. If it is the first time the game state is being retrieves, it writes over
 * the client state to ensure that the client state begins as the server state.
 * @param {any} overrideClientState Run during newgame to sync both states
 * @param {any} newgame Begins some of the long term running application during initialization
 */
function getgame(overrideClientState, newgame) {
    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = http.responseText;
            if (http.status === 500) {
                window.setTimeout(getgame(false, false), SERVER_READ_TIME);
            }
            else if (http.status < 400) {
                if (returnData) {
                    returnData = JSON.parse(returnData);
                    clientgamestate.roomdata = returnData;
                    gameArea.drawGame(clientgamestate.roomdata, overrideClientState);

                    if (newgame) {
                        window.setTimeout(getgame(false, false),0);
                        drawgamerefresh = setInterval(function () {
                            updategamewatcher();
                        }, CLIENT_REFRESH_TIME);
                        window.setTimeout(function () { toupdate(); }, 0);
                    }
                    else {
                        window.setTimeout(getgame(false, false), SERVER_READ_TIME);
                    }
                }
                else {
                status.innerHTML = "getgame error";
                window.setTimeout(getgame(false, false), SERVER_READ_TIME);
                }
            }
            else {
                document.getElementById("status").innerHTML = returnData;
                window.setTimeout(getgame(false, false), SERVER_READ_TIME);
            }
        }
    };
    http.open("GET", "api/Room/GetGame/?roomid=" + clientgamestate.roomid);
    http.send();
}

/**
 * Responsible for ending the game. Pings the current room to initiate a room ending.
 */
function endgame() {
    clearInterval(drawgamerefresh);

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = http.responseText;
            if (http.status < 400) {
                location.reload();
                if (returnData) {
                    document.getElementById("status").innerHTML = "Successfully logged out";
                }
                else { status.innerHTML = " endgame error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };

    http.open("GET", "api/Room/EndGame/?playerid=" + clientgamestate.playerid + "&roomid=" + clientgamestate.roomid);
    http.send();
}

/**
 * Statistics Section
 * getPlayerstats
 */

function getPlayerStats() {

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = http.responseText;
            if (http.status < 400) {
                if (returnData) {
                    returnData = JSON.parse(returnData);
                    alert("Number of Accounts: " + returnData.numAccounts +
                        "\nNumber of Players Logged in: " + returnData.numLoggedIn +
                        "\nAverage Num Logins / Person: " + returnData.avgNumLogins +
                        "\nAverage Account Age (seconds) :" + returnData.avgAccountAge);
                }
                else { status.innerHTML = "stats error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };

    http.open("GET", "api/Player/GetStats/");
    http.send();
}