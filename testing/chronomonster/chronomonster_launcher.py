"""PyInstaller-safe launcher kept outside the package namespace."""

from chronomonster.cli import main

raise SystemExit(main())

