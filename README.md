---
services: service-fabric
platforms: dotnet
author: t-anmena
---

# Service Fabric Online Game Application [WorkBud]
This reference application demonstrates how to begin building an end-to-end online multiplayer game on Service Fabric by taking advantage of Reliable Services. This application will help demonstrate any type of online application, but implements some solutions that meet the requirements of modern real-time MMOs.

## Scenario
The context of this sample is an online presence-based game in which users can login and join rooms. In these rooms users can see every other player in the room, move around the room, and change features about themselves. Accounts and player information are persisted, and players can join any room they would like at each new login. This sample application was very simply designed yet able to scale massively, so there are three key services:

- Web front-end Service
- Offline Player Service (Cold Storage)
- Online Rooms Service (Hot Storage)

The web service routes requests from clients to the appropriate services and performs some data validation. The offline player service holds long term player data like metrics and player settings and holds game data when that player is offline. Upon login, that game data is moved to the online rooms service, which stores that player data in a room with other players, so that data can be quickly gathered and updated. Upon logout, that data is moved back to cold storage.

Using Service Fabric's stateful services, each of these services can maintain its own data, rather than relying a shared monolithic data base. This allows each service to scale independently using Service Fabric's stateful partitioning to meet its unique requirements for data capacity and throughput. In terms of gameplay, this also decreases the distance of the data to the client, which means faster response times, important for online games.

## Running this sample
This application is only depends on Service Fabric, meaning deployment should be a breeze:

1. Download and install the Visual Studio 2017 Service Fabric SDK [here][1].
2. Open the .sln solution file in Visual Studio 2017.
3. Press F5 to run.
4. Open the application from the local web endpoint: `localhost:8081`

## Deploying to cloud
Deploying the application to the cloud is as easy as deploying it on your local box:

1. Create a cluster to host your Service Fabric application. If you want to deploy to your own endpoint, skip to step `2`. To create a cluster on Azure, use the guide that can be found [here][2]. Below are the minimum necessary settings for this application:

![Cluster Creation Image][options1]

If you are deploying the application for personal use only, you can choose to leave the cluster unsecured.

2. Edit the file at `SampleGame/SampleGame/ApplicationParameters/Cloud.xml`. Where it says `<ClusterConnectionParameters ConnectionEndpoint=""/>` enter the client endpoint provided to you by the cluster you set up.

3. Right-click the application project in Visual Studio and select "Publish". Choose your cloud publish profile:

![Cloud Parameter Image][publish]

Enter the client endpoint that you just provided to your `Cloud.xml`.

4. Connect to the game at your endpoint, at port 8081

## Next Steps
This application was designed to be readable to someone without prior knowledge of Service Fabric. All important functions are documented XML-style in /docs, and there are also some general documents to support the readings:
- [Learn about the application architecture and data flow.](../blob/master/docs/architecture.md)
- [Walk through the implementation of a new feature in the game.](../blob/master/docs/newfeature.md)
- [Deploy the application to the cloud.](../blob/master/docs/cloud.md)

[1]: https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-get-started
[2]: https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-cluster-creation-via-portal#create-cluster-in-the-azure-portal
[options1]: ../master/docs/media/options1.png
[publish]: ../master/docs/media/publish.png
