// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Authored by Antonio Menarde.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

var gameArea = {
    canvas: document.createElement("canvas"),
    start: function() {
        //cross browser reach for client window size
        var w = window.innerWidth || document.documentElement.clientWidth || document.body.clientWidth;
        var h = window.innerHeight || document.documentElement.clientHeight || document.body.clientHeight;
        var side = Math.min(w, h);
        side = parseInt(side * 0.9);
        this.canvas.width = side;
        this.canvas.height = side;
        this.canvas.position = "absolute";

        //try to position canvas in center of screen
        if (w > h) {
            this.canvas.style.left = (w - side) / 2;
            this.canvas.style.top = 0;
        } else {
            this.canvas.style.top = (h - side) / 2;
            this.canvas.style.left = 0;
        }

        this.context = this.canvas.getContext("2d");

        //insert the canvas before the status bar
        document.body.insertBefore(this.canvas, document.getElementElementById("status_bar"));

        //add in the keylisteners used for moving around the game
        window.addEventListener("keydown",
            function(e) {
                gameArea.keys = (gameArea.keys || []);
                gameArea.keys[e.keyCode] = true;

            });
        window.addEventListener("keyup",
            function(e) {
                gameArea.keys[e.keyCode] = false;
            });
    },

    //clears the game canvas during redraw
    clear: function() {
        this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
    },

    //the primary draw game loop
    drawGame: function(returnData, overrideClientState) {
        gameArea.clear();

        //Figure out which room to draw
        if (clientgamestate.RoomType === "Office") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/office.png");
        } else if (clientgamestate.RoomType === "Garden") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/garden.png");
        } else if (clientgamestate.RoomType === "Cafe") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/cafe.png");
        } else {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/room.png");
        }

        //We check for an inactivity logout by making sure that the getgame has our data, if it doesn't, we were logged out
        var foundMyself = false;

        clientgamestate.RoomData = returnData;
        for (var i = 0; i < returnData.length; i++) {

            //This function forces client to match server state when necessary, like login
            if (returnData[i].Key === clientgamestate.playerid) {
                foundMyself = true;

                if (overrideClientState === true) {
                    clientgamestate.XPos = returnData[i].Value.XPos;
                    clientgamestate.YPos = returnData[i].Value.YPos;
                    clientgamestate.Color = returnData[i].Value.Color;
                }
                servergamestate.XPos = returnData[i].Value.XPos;
                servergamestate.YPos = returnData[i].Value.YPos;
                servergamestate.Color = returnData[i].Value.Color;
                playerDraw(
                    this.context,
                    parseInt(Math.min(this.canvas.width, this.canvas.height) * 0.1),
                    parseInt(this.canvas.width * clientgamestate.XPos / 100),
                    parseInt(this.canvas.height * (clientgamestate.YPos / 100)),
                    returnData[i].Key,
                    clientgamestate.Color
                );
            } else {
                playerDraw(

                    //draw the player, for each player

                    this.context,
                    parseInt(Math.min(this.canvas.width, this.canvas.height) * 0.1),
                    parseInt(this.canvas.width * (returnData[i].Value.XPos / 100)),
                    parseInt(this.canvas.height * (returnData[i].Value.YPos / 100)),
                    returnData[i].Key,
                    returnData[i].Value.Color);
            }
        }

        if (!foundMyself) {
            location.reload();
        }
    }
};


function roomDraw(ctx, side, src) {

    this.image = new Image();
    this.image.src = src;
    this.side = side;

    ctx.drawImage(this.image, 0, 0, this.side, this.side);
}

function playerDraw(ctx, side, x, y, name, color) {

    //this draws the enterior colored part of the character
    ctx.beginPath();
    ctx.moveTo(x, y + side);
    ctx.quadraticCurveTo(parseInt(x + side / 2.0), y, x + side, y + side);
    ctx.quadraticCurveTo(x + side, y + side + 10, parseInt(x + side / 2.0), y + side + 12);
    ctx.quadraticCurveTo(x, y + side + 10, x, y + side);
    ctx.closePath();
    ctx.lineWidth = 4;
    ctx.stroke();
    ctx.fillStyle = "#".concat(color);
    ctx.fill();

    //this draws the outline and eyes of the player
    ctx.beginPath();
    ctx.arc(parseInt(x + side / 3.0), parseInt(y + side / 1.2), 4, 0, 2 * Math.PI, false);
    ctx.arc(parseInt(x + 2 * side / 3.0), parseInt(y + side / 1.2), 4, 0, 2 * Math.PI, false);
    ctx.fillStyle = "black";
    ctx.fill();
    ctx.closePath();


    //this draws the name of the player under the player
    ctx.font = "16px Arial";
    ctx.fillStyle = color;
    var startx = Math.max(0, parseInt(x + (side - ctx.measureText(name).width) / 2.0));
    ctx.fillText(name, startx, y + side + 28);
}