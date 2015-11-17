The peer-to-peer class is currently nothing more than a `NetServer` with the added possibility to initiate connections, ie. explicitly connect to other peers. Also added is the possibility to call `DiscoverLocalPeers()` which works like the `DiscoverLocalServers()` of `NetClient`.

Input on `NetPeer` features are appreciated. NAT traversal support is currently the only thing planned.