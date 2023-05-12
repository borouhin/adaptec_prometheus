A simple textfile collector for Prometheus node_exporter that gathers basic statistics about Adaptec RAID controllers using Linux arcconf command-line utility.

It's dependant on the output format of particular arcconf version, so please use the latest v. 3.10.24308 (https://adaptec.com/en-us/speed/raid/storage_manager/arcconf_v3_10_24308_zip.php)
It assumes that arcconf is installed at /usr/local/bin

Currently only 1 controller is supported and only following metrics are collected:
* Controller status and temperature
* Controller battery status and temperature
* Number of defunct drives
* Number of logical drives (total / failed / degraded) 
* SMART attributes for all drives connected to the controller.

It should be straightforward to add support for multiple controllers and additional metrics if required.

It's C#, so it either requires .NET to be installed on the Linux machine (see https://learn.microsoft.com/en-us/dotnet/core/install/linux), or it can be built as a standalone executable (~15 Mb).

To use this textfile collector, add a cronjob for root user which redirect adaptec_prometheus output to /var/lib/prometheus/node-exporter/adaptec_raid.prom, like this (to run the job every 5 minutes):
```
*/5 * * * * /usr/local/bin/adaptec_prometheus >/var/lib/prometheus/node-exporter/adaptec_raid.prom
```
Sample Ansible tasks to deploy this collector may look like this (correct the paths in accordance with your setup):
```
- name: Adaptec Text Collector
  become: true
  block:
    - name: Install arcconf
      ansible.builtin.copy:
        src: /home/{{ ansible_user }}/.ansible_temp/node_exporter/arcconf
        dest: /usr/local/bin/arcconf
        mode: 0755
    - name: Install collector
      ansible.builtin.copy:
        src: "/home/{{ ansible_user }}/.ansible_temp/node_exporter/adaptec_prometheus"
        dest: /usr/local/bin/adaptec_prometheus
        mode: 0755
    - name: Cronjob for collector
      ansible.builtin.cron:
        name: "adaptec_prometheus"
        job: "/usr/local/bin/adaptec_prometheus >/var/lib/prometheus/node-exporter/adaptec_raid.prom"
        minute: "*/5"
```
