FROM registry.k8s.io/kubectl:v1.32.2 AS kubectl
FROM restic/restic:0.19.1
USER root
COPY --from=kubectl /bin/kubectl /usr/local/bin/kubectl
COPY deploy/backup/backup.sh /usr/local/bin/slate-backup
RUN chmod 0555 /usr/local/bin/slate-backup
ENTRYPOINT ["/usr/local/bin/slate-backup"]
