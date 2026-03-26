import { FieldErrors } from "react-hook-form"
import { EcdSystemUserUpdateRequest } from "@/types/api.types"

interface UseEcdSystemUserUpdateFieldsProps {
  errors: FieldErrors<EcdSystemUserUpdateRequest>
  disabledFields?: string[]
}

export default function useEcdSystemUserUpdateFields(props: UseEcdSystemUserUpdateFieldsProps) {
  const { errors, disabledFields = [] } = props

  return {
    mobileNumber: {
      props: {
        id: "system-user-mobileNumber",
        name: "mobileNumber",
        heading: "Mobile Number",
        type: "phone" as const,
        placeholder: "Enter mobile number...",
        disabled: disabledFields.includes("mobileNumber"),
        error: errors.mobileNumber,
      },
      rules: { },
    },
  }
}
