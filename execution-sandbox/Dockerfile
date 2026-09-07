# A deliberately minimal execution image. Invocation must be mediated by a service that supplies
# an ephemeral /tmp work item and enforces its own timeout; no user files are baked into the image.
FROM alpine:3.20
RUN addgroup -S runner && adduser -S runner -G runner
USER runner
WORKDIR /tmp
CMD ["sleep", "infinity"]
