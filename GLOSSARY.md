# Local Network Monitoring

This project concerns visibility into internet bandwidth usage on a network the
user owns or administers.

## Language

**Internet traffic**:
Traffic crossing the monitored network's gateway to or from the internet.
_Avoid_: All network traffic

**Local traffic**:
Traffic between devices within the monitored network that does not cross its
internet gateway.
_Avoid_: Internet traffic

**Per-device internet usage**:
The amount or rate of internet traffic attributed to an individual device.
_Avoid_: All traffic, connection contents

**Gateway**:
The network boundary through which monitored devices reach the internet.
_Avoid_: Monitoring computer

**Internet access pause**:
A reversible restriction on a selected device's access to the internet, without
necessarily disconnecting it from Wi-Fi or preventing local traffic.
_Avoid_: Kick off the network, disconnect from Wi-Fi

**Download rate**:
The rate of internet traffic received by a monitored device.
_Avoid_: Total usage

**Upload rate**:
The rate of internet traffic sent by a monitored device.
_Avoid_: Total usage

**Device entry**:
A distinct network identity reported by the gateway, optionally given a friendly
name by the user. Multiple entries may represent the same physical device.
_Avoid_: Person, uniquely identified physical device

**Monitoring session**:
The period during which the foreground app is running and collecting bandwidth
measurements.
_Avoid_: Always-on monitoring

**Protected device entry**:
A device entry that is ineligible for an internet access pause, either to preserve
the management path or because the user has explicitly protected it.
_Avoid_: Trusted device

**Measurement coverage**:
The set of device entries for which attributable internet usage measurements are
available.
_Avoid_: Complete network visibility
