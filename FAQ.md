##### Q: Is there a plan for the developement of the library? #####
A: Not really. I aim to squash all the bugs and performance issues out of the code and look at the [missing](Missing.md) pieces at the same time.

##### Q: What's new in this version? #####
A: Most of the code is rewritten from scratch, but the general layout of classes remain the same as the first version. The biggest change is that the library now runs in its own thread, better utilizing todays multicore processors. A few API changes has been done to accomodate this; for example, all the events have been removed. `Status` changes and debug log messages are delivered using the same mechanism as data retrieval.

Here's a list of things which have been improved:

  * Library runs in separate thread
  * `NetPeer` class for peer-to-peer networking
  * Increased robustness in high lag, high loss situations
  * Increased robustness/correctness during connecting and disconnecting
  * Extensive internal object pooling to reduce allocations
  * Less allocations during writing/reading messages
  * Faster connection lookup and duplicate detection code
  * Call chain and virtual calls reduced
  * Extended congestion control
  * Better detection and handling of forcibly closed connections
  * Extended statistics
  * Generally leaner and meaner code

##### Q: What `NetChannel` should I use? #####
A: Assuming you've read the description of the different types but still can't decide; here's what you need to ask yourself:

  * Does the data definately absolutely have to arrive?
  * - Yes
  * -  - MUST all messages arrive in the exact order they were sent?
  * -  -  - Yes - Use `NetChannel.ReliableOrdered`
  * -  -  - No - Use `NetChannel.ReliableUnordered`
  * - No
  * -  - What happens if a message is delayed, and arrives AFTER a newer message has already arrived?
  * -  -  - The old data should be discarded - Use `UnreliableOrdered`
  * -  -  - The old data should be delivered - Use `Unreliable`

Here are some examples:
Typical `Unreliable` message: Player waves hello
Typical `UnreliableOrdered` message: Player health and ammo count
Typical `ReliableUnordered` message: Player fires a missile
Typical `ReliableOrdered` message: Player sends a chat message


##### Q: What's the difference between `UnreliableOrdered4` and `UnreliableOrdered5`? #####
A: ... or `ReliableOrdered2` and `ReliableOrdered3`? It's different channels. Let's take an example:
In your game you have a Health meter and an Ammo meter. You've determined that `UnreliableOrdered` delivery method is the best (ie. "old" messages are dropped).
Now let's say you send a Health message, but it's delayed by bad network conditions. If a later Ammo message arrived before our health message... the health message will be dropped; since it's considered "old".

This is not our intention... old Health data is still relevant even if we've received newer Ammo data.

So we send them in different channels. Health messages in `UnreliableOrdered3` and Ammo in `UnreliableOrdered4` - that way they won't interfere with eachother.
The same logic applied to `ReliableOrdered`. Messages in one channel won't be withheld because of dropped messages in another channel.