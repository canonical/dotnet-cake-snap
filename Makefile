# Makefile for dotnet-cake snap packaging
#
# Generates snap/snapcraft.yaml from snap/local/template.snapcraft.yaml by
# replacing {{VERSION}}, {{GRADE}}, {{CONFINEMENT}} and {{BRANCH}} placeholders.

# Variables with defaults (overridable via command line: make VERSION=x.y.z)
VERSION     ?=
GRADE       ?= stable
CONFINEMENT ?= classic
BRANCH      ?= main

# Paths
SNAP_DIR            := snap
SNAPCRAFT_TEMPLATE  := $(SNAP_DIR)/local/template.snapcraft.yaml
SNAPCRAFT_YAML      := $(SNAP_DIR)/snapcraft.yaml

# Phony targets
.PHONY: all build clean fetch-version generate-snapcraft pack help

# Default target
all: build

# Main build target - depends on all steps
build: fetch-version generate-snapcraft pack
	@echo "Snap package build completed successfully!"

# Fetch latest Cake release from GitHub if VERSION not provided.
# Strips the leading 'v' from the tag (e.g. v6.2.0 -> 6.2.0).
fetch-version:
ifeq ($(VERSION),)
	@echo "Fetching latest Cake release from GitHub..."
	$(eval GITHUB_API_HEADER := $(if $(GITHUB_TOKEN),-H "Authorization: Bearer $(GITHUB_TOKEN)",))
	$(eval VERSION := $(shell curl -s $(GITHUB_API_HEADER) https://api.github.com/repos/cake-build/cake/releases/latest | jq -r '.tag_name' | sed 's/^v//'))
	@if [ -z "$(VERSION)" ] || [ "$(VERSION)" = "null" ]; then \
		echo "Error: could not fetch latest Cake version from GitHub" >&2; \
		exit 1; \
	fi
	@echo "Latest Cake version: $(VERSION)"
else
	@echo "Using provided version: $(VERSION)"
endif

# Generate snap/snapcraft.yaml from the template, replacing placeholders.
# Uses single quotes around values so sed treats them literally (handles
# versions/SHAs containing characters like '+' or '.').
generate-snapcraft:
	@echo "Generating $(SNAPCRAFT_YAML) with version $(VERSION), grade $(GRADE), confinement $(CONFINEMENT), branch $(BRANCH)..."
	@cp $(SNAPCRAFT_TEMPLATE) $(SNAPCRAFT_YAML)
	@sed -i 's/{{VERSION}}/$(VERSION)/g' $(SNAPCRAFT_YAML)
	@sed -i 's/{{GRADE}}/$(GRADE)/g' $(SNAPCRAFT_YAML)
	@sed -i 's/{{CONFINEMENT}}/$(CONFINEMENT)/g' $(SNAPCRAFT_YAML)
	@sed -i 's/{{BRANCH}}/$(BRANCH)/g' $(SNAPCRAFT_YAML)
	@echo "Generated $(SNAPCRAFT_YAML)"

# Run snapcraft pack
pack:
	@echo "Running snapcraft pack --verbose..."
	snapcraft pack --verbose
	@echo "Snapcraft pack completed!"

# Clean generated files
clean:
	snapcraft clean
	@rm -f $(SNAPCRAFT_YAML)
	@rm -f *.snap
	@echo "Cleaned generated files"

# Help target
help:
	@echo "dotnet-cake Snap Build System"
	@echo ""
	@echo "Usage:"
	@echo "  make                                    # Build with latest GitHub release"
	@echo "  make VERSION=6.2.0                      # Build with specific version"
	@echo "  make VERSION=6.2.0 GRADE=devel          # Build with custom grade"
	@echo "  make VERSION=abc1234 GRADE=devel BRANCH=develop  # Edge build from a commit SHA"
	@echo "  make clean                              # Remove generated files"
	@echo ""
	@echo "Variables:"
	@echo "  VERSION     - Cake version (default: fetched from GitHub releases/latest)"
	@echo "  GRADE       - Snap grade (default: stable)"
	@echo "  CONFINEMENT - Snap confinement (default: classic)"
	@echo "  BRANCH      - Branch label stamped into AssemblyInformationalVersion (default: main)"
