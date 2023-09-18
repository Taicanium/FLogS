# Known Issues
- The program may finish the salvage routine normally, and then open the same file and overwrite it with garbage. This only happens on some machines, and the cause is completely unknown. A stopgap is currently in place that prevents the program from opening the same file twice during a single routine. But this won't solve anything if the garbage somehow comes first.
