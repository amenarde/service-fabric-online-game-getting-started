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
const CLIENT_REFRESH_TIME = 35; var drawgamerefresh; 
const SERVER_READ_TIME = 75; var getgamerefesh;    
const SERVER_PUSH_TIME = 100; var updategamerefresh;

boolean okToUpdate = true;

function test() {var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = http.responseText;
            document.getElementById("status").innerHTML = returnData;
        }
    };
    

    http.open("GET", "api/Player/Test/?text=hello");
    http.send();
}


// These structures are used to manage the relevant game state for the client and effectively communicate with the controller.
var clientgamestate = {
    'playerid': null,
    'roomid': null,
    'xpos': 0,
    'ypos': 0,
    'color': null,
    'roomdata': null
};
var servergamestate = {
    'xpos': 0,
    'ypos': 0,
    'color': null
};


/**
 * On window load hide irrelevant divs to maintain order and retrieve rooms from backend.
 */
window.onload = function () {
    document.getElementsByClassName("gameDiv")[0].style.display = 'none';
    showrooms();
};

window.addEventListener('beforeunload', function (event) {
    endgame(clientgamestate.playerid, clientgamestate.roomid);
}, false);

/**
 * This function looks for key presses so that it knows to update the clients game view. This function is designed to allow for smooth
 * gameplay for the player. Actually updating the game happens in a different loop.
 */
function updategamewatcher() {

    if (gameArea.keys && gameArea.keys[37]) {
        clientgamestate.xpos -= 1;
        //this passes in a copy of the game state, reference does not garauntee consistency for duration of draw function
        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    else if (gameArea.keys && gameArea.keys[39]) {
        clientgamestate.xpos += 1;
        //this passes in a copy of the game state, reference does not garauntee consistency for duration of draw function
        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    if (gameArea.keys && gameArea.keys[38]) {
        clientgamestate.ypos -= 1;
        //this passes in a copy of the game state, reference does not garauntee consistency for duration of draw function
        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }
    else if (gameArea.keys && gameArea.keys[40]) {
        clientgamestate.ypos += 1;
        //this passes in a copy of the game state, reference does not garauntee consistency for duration of draw function
        gameArea.drawGame(JSON.parse(JSON.stringify(clientgamestate.roomdata)), false);
    }

}


// Checks whether the client's state has deviated from the last known server state, which would prompt an update request.
function toupdate() {
    if (clientgamestate.xpos !== servergamestate.xpos || clientgamestate.ypos !== servergamestate.ypos) {
        updategame(true, false);
    }
}

/**
 * Sends a request to the controller to establish a new game.
 * sets status message on the game's status bar
 * initializes the game canvas and retrieves the first server state that sets the client state
 * @argument {boolean} bool true if chooosing an already existing room, false if a new room
 * @argument {button} bool so that the function can reach in and get the name of the room
 */
function newgame(bool, context) {

    if (bool == true) {
        var roomid = context.name;
    }
    else {
        var roomid = document.getElementById("newgamename").value;
    }

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
                if (returnData) {
                    document.getElementById("status").innerHTML = "Successfully logged in";

                    clientgamestate.playerid = document.getElementById("pidform").value;
                    clientgamestate.roomid = roomid;

                    //initialize canvas
                    gameArea.start();

                    //run getgame in the "new game" setting
                    getgame(true, true);

                    //hide login-related html
                    document.getElementsByClassName("gameDiv")[0].style.display = '';
                    document.getElementsByClassName("loginDiv")[0].style.display = 'none';
                }
                else { status.innerHTML = "newgame error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };

    http.open("GET", "api/Player/NewGame/?playerid=" + document.getElementById("pidform").value + "&roomid=" + roomid);
    http.send();
}



function showrooms() {
    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
                if (returnData) {

                    var table = document.getElementById('roomstable');

                    while (table.childNodes[1].childElementCount > 1) {
                        table.removeChild(table.lastChild);
                    }

                    //Header
                    var row = table.insertRow();
                    var namecell = row.insertCell(); var name = document.createTextNode("  Room Name  "); namecell.appendChild(name);
                    var typecell = row.insertCell(); var type = document.createTextNode("  Room Type  "); typecell.appendChild(type);
                    var numcell = row.insertCell(); var num = document.createTextNode("# Players"); numcell.appendChild(num);
                    var buttoncell = row.insertCell(); var button = document.createTextNode("Choose / Create"); buttoncell.appendChild(button);


                    for (var i = 0; i < returnData.length; i++) {
                        row = table.insertRow();
                        namecell = row.insertCell(); name = document.createTextNode(returnData[i].Key); namecell.appendChild(name);
                        typecell = row.insertCell(); type = document.createTextNode(returnData[i].Value.roomtype); typecell.appendChild(type);
                        numcell = row.insertCell(); num = document.createTextNode(returnData[i].Value.numplayers); numcell.appendChild(num);

                        buttoncell = row.insertCell();
                        button = document.createElement("button");
                        button.setAttribute('type', 'button');
                        button.setAttribute('name', returnData[i].Key);
                        button.setAttribute('onclick', 'newgame(true, this)');
                        buttoncell.appendChild(button);
                    }

                    row = table.insertRow();

                    namecell = row.insertCell();
                    name = document.createElement('INPUT');
                    name.setAttribute('type', 'text');
                    name.setAttribute('id', 'newgamename');
                    name.setAttribute('placeholder', 'Room Name');
                    namecell.appendChild(name);

                    typecell = row.insertCell();
                    type = document.createElement('INPUT');
                    type.setAttribute('type', 'text');
                    type.setAttribute('placeholder', 'Room Type');
                    typecell.appendChild(type);

                    numcell = row.insertCell(); num = document.createTextNode(""); numcell.appendChild(num);

                    buttoncell = row.insertCell();
                    button = document.createElement("button");
                    button.setAttribute('type', 'button');
                    button.setAttribute('id', "newgamebutton")
                    button.setAttribute('onclick', 'newgame(false, this)');
                    buttoncell.appendChild(button);

                }
                else {
                    status.innerHTML = "getgame error";
                }
            }
            else {
                document.getElementById("status").innerHTML = returnData;
            }
        }
    };
    http.open("GET", "api/Room/GetRooms");
    http.send();
}

function updategame(updatepos, updatefeat) {

    var colorchange; var xchange; var ychange;

    if (updatefeat) {
        if (document.getElementById("colorupdateform").value === "") {
            colorchange = clientgamestate.color;
        }
        else {
            colorchange = document.getElementById("colorupdateform").value;
            if (!colorchange.startsWith('#')) { colorchange = '#'.concat(colorchange); }
            var validcolor = /(^#[0-9A-F]{6}$)|(^#[0-9A-F]{3}$)/i.test(colorchange);
            if (!validcolor) { //blackbox regex that verifies color formatting
                document.getElementById("status").innerHTML = "not a valid color code";
                return;
            }
            colorchange = colorchange.substring(1);
            clientgamestate.color = colorchange;
            document.getElementById("colorupdateform").value = "";
        }
    }
    else {
        colorchange = clientgamestate.color;
    }

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
                if (returnData) {
                    document.getElementById("status").innerHTML = "Successfully updated game";
                }
                else { status.innerHTML = " updategame error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData.value;
            }
        }
    };

    if (updatepos || updatefeat) {
        http.open("GET", "api/Room/UpdateGame/?playerid=" + clientgamestate.playerid + "&roomid=" + clientgamestate.roomid +
            "&xpos=" + clientgamestate.xpos + "&ypos=" + clientgamestate.ypos + "&color=" + colorchange);
        http.send();
    }
}

function getgame(overrideClientState, newgame) {
    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
                if (returnData) {
                    clientgamestate.roomdata = returnData;
                    gameArea.drawGame(clientgamestate.roomdata, overrideClientState);

                    if (newgame) {
                        getgamerefresh = setInterval(function () {
                            getgame(false, false);
                        }, SERVER_READ_TIME);
                        drawgamerefresh = setInterval(function () {
                            updategamewatcher();
                        }, CLIENT_REFRESH_TIME);
                        updategamerefresh = setInterval(function () {
                            toupdate();
                        }, SERVER_PUSH_TIME);
                    }
                }
                else { status.innerHTML = "getgame error"; }
            }
            else {
                document.getElementById("status").innerHTML = returnData;
            }
        }
    };
    http.open("GET", "api/Room/GetGame/?roomid=" + clientgamestate.roomid);
    http.send();
}

function endgame() {
    clearInterval(getgamerefresh);
    clearInterval(updategamerefresh);
    clearInterval(updategamerefresh);

    var http = new XMLHttpRequest();
    http.onreadystatechange = function () {
        if (http.readyState === 4) {
            returnData = JSON.parse(http.responseText);
            if (http.status < 400) {
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

    http.open("GET", "api/RoomManager/EndGame/?playerid=" + clientgamestate.playerid + "&roomid=" + clientgamestate.roomid);
    http.send();

    location.reload();
}
