# Concepts
Below are some concepts relevant to understanding how this application works. Provided are the name of the concept, where it is used, and a link to the original article on the topic. If you browse through the application code, you will see a lot of repeated terminology. You can learn the common terms here:

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-technical-overview

### Microservices
This application is built using microservices, which is a different paradigm than traditional monolithic applications. Microservices allow applications to scale different parts of themselves independently, which is often more efficient. They also allow for application to be built quicker, upgraded more easily, and adapted to different problems more easily.

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-overview-microservices

### Service Fabric
Service Fabric is the platform used to build this microservice application. Service Fabric handles scaling the application, managing failover scenarios, and upgrading the application. When it comes down to it, Service Fabric allows you to take full advantage of the best parts of building with microservices, while promising very little downtime in any scenario.

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-content-roadmap

### Service Fabric: Reliable Services
The second part of Service Fabric leveraged in this application are Reliable Services. 'PlayerManager' and 'RoomManager' are both reliable services. This application does not use any external database -- all the account information is stored in Reliable Collections managed by these services. The advantages using Reliable Services brings to this application is that data takes less time to retrieve, can survive through failover, and can massively scale if necessary.

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-services-introduction

### Reliable Services: Reliable Collections
If you look at the code in `RoomStoreController` or `PlayerStoreController` you will see extensive use of `ReliableDictionary`, which is much like a `ConcurrentDictionary`, except that it leverages Reliable Services. The most important things to understand when using a Reliable Collection is that all interactions with the collection must be done through a transaction, which ensures that your actions are **ACID**. During the transaction, you will be taking different types of locks on the data. These concepts are explained here:

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-services-reliable-collections

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-services-reliable-collections-transactions-locks

### Building with Reliable Collections
If you want to use Reliable Collections in your application or build on this application, understanding correct coding patterns and avoiding common pitfalls will help you use them more effectively. These links offer insight into working with Reliable Collections:

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-services-lifecycle

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-work-with-reliable-collections

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-services-advanced-usage

### Service Communication: Reverse Proxy
This application consists of three services, each of which must send and receive messages from the other two, and should be accessible by the actual player's client. This is done using Reverse Proxy. Reverse Proxy takes requests, and then routes them to the right place. It resolves exactly where that request needs to do by locating the VM with the right service, the right partition of that service, and the replica that is the primary for that parititon. It is effective and supports HTTPS.

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reverseproxy

It is not the only way to communicate between services, however. You can use the most basic 'ServiceProxy' or define your own protocol:

https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-connect-and-communicate-with-services

## Further Reading

Service Fabric is capable of a lot more than the paradigms used in this simple sample application. Here are other useful options:

- [Reliable Actors](https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-reliable-actors-introduction)
- [Containers](https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-containers-overview)
- [Monitoring Applications](https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-diagnostics-overview)
