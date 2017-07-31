// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Authored by Antonio Menarde.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

var gameArea = {
    canvas: document.createElement("canvas"),
    start: function () {
        //cross browser reach for client window size
        //==================================================
        var w = window.innerWidth || document.documentElement.clientWidth || document.body.clientWidth;
        var h = window.innerHeight || document.documentElement.clientHeight || document.body.clientHeight;
        var side = Math.min(w, h);
        side = parseInt(side * 0.9);
        this.canvas.width = side;
        this.canvas.height = side;
        this.context = this.canvas.getContext("2d");
        //==================================================

        document.body.insertBefore(this.canvas, document.getElementsByClassName('status_bar')[0]);
        window.addEventListener('keydown', function (e) {
            gameArea.keys = (gameArea.keys || []);
            gameArea.keys[e.keyCode] = true;
        })
        window.addEventListener('keyup', function (e) {
            gameArea.keys[e.keyCode] = false;
        })
    },
    clear: function () {
        this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
    },
    drawGame: function (returnData, overrideClientState) {
        gameArea.clear();

        //Figure out which room to draw
        if (clientgamestate.roomtype === "Office") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/office.png");
        }
        else if (clientgamestate.roomtype === "Garden") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/garden.png");
        }
        else if (clientgamestate.roomtype === "Cafe") {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/cafe.png");
        }
        else {
            roomDraw(this.context, Math.min(this.canvas.width, this.canvas.height), "img/room.png");
        }
        

        clientgamestate.roomdata = returnData;
        for (var i = 0; i < returnData.length; i++) {
            //This function forces client to match server state when necessary, like login
            if (returnData[i].Key === clientgamestate.playerid) {
                if (overrideClientState === true) {
                    clientgamestate.xpos = returnData[i].Value.xpos;
                    clientgamestate.ypos = returnData[i].Value.ypos;
                    clientgamestate.color = returnData[i].Value.color;
                }
                servergamestate.xpos = returnData[i].Value.xpos;
                servergamestate.ypos = returnData[i].Value.ypos;
                servergamestate.color = returnData[i].Value.color;
                playerDraw(
                    this.context,
                    parseInt(Math.min(this.canvas.width, this.canvas.height) * 0.1),
                    parseInt(this.canvas.width * clientgamestate.xpos / 100),
                    parseInt(this.canvas.height * (clientgamestate.ypos / 100)),
                    returnData[i].Key,
                    clientgamestate.color
                );
            }
            else {
                playerDraw(
                    this.context,
                    parseInt(Math.min(this.canvas.width, this.canvas.height) * 0.1),
                    parseInt(this.canvas.width * (returnData[i].Value.xpos / 100)),
                    parseInt(this.canvas.height * (returnData[i].Value.ypos / 100)),
                    returnData[i].Key,
                    returnData[i].Value.color);
            }
        }
    }
}


function roomDraw(ctx, side, src) {

    this.image = new Image();
    this.image.src = src;
    this.side = side;

    ctx.drawImage(this.image, 0, 0, this.side, this.side);
}

function playerDraw(ctx, side, x, y, name, color) {

    ctx.beginPath();
    ctx.moveTo(x, y + side);
    ctx.quadraticCurveTo(parseInt(x + (side / 2.0)), y, x + side, y + side);
    ctx.quadraticCurveTo(x + side, y + side + 10, parseInt(x + (side / 2.0)), y + side + 12);
    ctx.quadraticCurveTo(x, y + side + 10, x, y + side);
    ctx.closePath();
    ctx.lineWidth = 4;
    ctx.stroke();
    ctx.fillStyle = '#'.concat(color);
    ctx.fill();

    ctx.beginPath();
    ctx.arc(parseInt(x + (side / 3.0)), parseInt(y + (side / 1.2)), 4, 0, 2 * Math.PI, false);
    ctx.arc(parseInt(x + 2*(side / 3.0)), parseInt(y + (side / 1.2)), 4, 0, 2 * Math.PI, false);
    ctx.fillStyle = 'black';
    ctx.fill();
    ctx.closePath();


    //ctx.drawImage(image, x, y + 50, side, parseInt(side * 0.75));

    ctx.font = "16px Arial";
    ctx.fillStyle = "white";
    var startx = Math.max(0, parseInt(x + (side - ctx.measureText(name).width)/2.0));
    ctx.fillText(name, startx, y + side + 35);
}