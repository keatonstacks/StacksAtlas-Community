import React from "react";
import { Box, Typography, Divider } from "@mui/material";

import { APP_VERSION } from "../constants/version";

interface PageHeaderProps {
  title: string;
  subtitle: string;
  stats: { label: string; value: string | number; color?: string }[];
  action?: React.ReactNode;
}

export const PageHeader = ({ title, subtitle, stats, action }: PageHeaderProps) => {

  // Helper to split title: First parts stay primary text, LAST word gets the primary color
  const words = title.split(" ");
  const mainTitle = words.slice(0, -1).join(" ");
  const lastWord = words[words.length - 1];

  return (
    <Box
      sx={{
        display: "flex",
        // Wraps on small screens to prevent title cutoff
        flexDirection: { xs: "column", md: "row" },
        justifyContent: "space-between",
        alignItems: { xs: "flex-start", md: "center" },
        mb: 4,
        gap: 2 // Gap prevents elements from touching if they wrap
      }}
    >
      <Box sx={{ maxWidth: { xs: "100%", md: "60%" } }}>
        <Typography
          variant="h3"
          sx={{
            color: "text.primary",
            letterSpacing: -1.5,
            lineHeight: 1.1,
            // Responsive font size
            fontSize: { xs: "1.25rem", sm: "1.5rem", md: "1.85rem", lg: "2.25rem" },
            textTransform: "uppercase"
          }}
        >
          {mainTitle}
          <Box component="span" sx={{ color: "primary.main", ml: 1 }}>
            {lastWord}
          </Box>
        </Typography>

        <Typography
          variant="caption"
          sx={{
            color: "text.secondary",
            letterSpacing: { xs: 1, sm: 2 },
            display: "block",
            mt: 0.5
          }}
        >
          STACKSATLAS v{APP_VERSION} • {subtitle}
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexShrink: 0 }}>
        {action}
        <Box sx={{
        bgcolor: "background.paper",
        px: 2.5, py: 1.5,
        borderRadius: 3,
        border: "1px solid",
        borderColor: "divider",
        display: "flex",
        // Ensure stats don't shrink too much
        minWidth: { md: "320px" },
        justifyContent: "space-around",
        gap: { xs: 2, sm: 3 }
      }}>
        {stats.map((stat, idx) => (
          <React.Fragment key={stat.label}>
            <Box sx={{ textAlign: 'center' }}>
              <Typography
                variant="caption"
                color="text.secondary"
                display="block"
                sx={{ fontSize: '0.6rem', fontWeight: 800, textTransform: 'uppercase' }}
              >
                {stat.label}
              </Typography>
              <Typography
                variant="body2"
                sx={{
                  color: stat.color || 'text.primary',
                  fontFamily: 'monospace',
                  fontSize: '1rem',
                  fontWeight: 600
                }}
              >
                {stat.value}
              </Typography>
            </Box>
            {idx < stats.length - 1 && (
              <Divider orientation="vertical" flexItem sx={{ mx: 0.5, height: 20, alignSelf: 'center' }} />
            )}
          </React.Fragment>
        ))}
      </Box>
      </Box>
    </Box>
  );
};
